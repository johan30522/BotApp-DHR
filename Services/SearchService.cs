using BotApp.Models;
using Google.Cloud.DiscoveryEngine.V1;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;



namespace BotApp.Services
{
    public interface ISearchService
    {
        Task<IReadOnlyList<ContextChunk>> RetrieveAsync(string query, CancellationToken ct = default);
    }
    public sealed class SearchService : ISearchService
    {
        private readonly SearchServiceClient _client;
        private readonly string _servingConfig;
        private readonly ILogger<SearchService> _logger;
        private readonly int _pageSize, _maxAns, _maxSeg;
        private readonly bool _enableAns;
        private readonly string _languageCode, _timeZone;

        public SearchService(IConfiguration cfg, ILogger<SearchService> logger)
        {
            _logger = logger;

            var projectId = cfg["GoogleCloud:ProjectId"];
            var location = cfg["GoogleCloud:Location"] ?? "global";

            // ✅ Ahora usamos el servingConfig de ENGINES (Integración > API)
            _servingConfig = cfg["GoogleCloud:Search:ServingConfig"]
                ?? throw new InvalidOperationException("Falta GoogleCloud:Search:ServingConfig");

            // Endpoint regional si no es global
            var endpoint = location == "global"
                ? "discoveryengine.googleapis.com"
                : $"{location}-discoveryengine.googleapis.com";

            _pageSize = int.TryParse(cfg["GoogleCloud:Search:PageSize"], out var ps) ? ps : 4;
            _enableAns = bool.TryParse(cfg["GoogleCloud:Search:EnableExtractiveAnswer"], out var ea) && ea;
            _maxAns = int.TryParse(cfg["GoogleCloud:Search:MaxExtractiveAnswerCount"], out var ma) ? ma : 1;
            _maxSeg = int.TryParse(cfg["GoogleCloud:Search:MaxExtractiveSegmentCount"], out var ms) ? ms : 4;
            _languageCode = cfg["GoogleCloud:Search:LanguageCode"] ?? "es-419";
            _timeZone = cfg["GoogleCloud:Search:TimeZone"] ?? "America/Costa_Rica";

            _client = new SearchServiceClientBuilder { Endpoint = endpoint }.Build();
        }

        public async Task<IReadOnlyList<ContextChunk>> RetrieveAsync(string query, CancellationToken ct = default)
        {
            try
            {
                var ecs = new SearchRequest.Types.ContentSearchSpec.Types.ExtractiveContentSpec
                {
                    MaxExtractiveSegmentCount = _maxSeg
                };

                if (_enableAns)
                {
                    // ⚠️ Solo agregar si tu Data Store NO usa chunking config
                    ecs.MaxExtractiveAnswerCount = _maxAns;
                }

                var req = new SearchRequest
                {
                    ServingConfig = _servingConfig,
                    Query = query,
                    PageSize = _pageSize,
                    QueryExpansionSpec = new SearchRequest.Types.QueryExpansionSpec
                    {
                        Condition = SearchRequest.Types.QueryExpansionSpec.Types.Condition.Auto
                    },
                    SpellCorrectionSpec = new SearchRequest.Types.SpellCorrectionSpec
                    {
                        Mode = SearchRequest.Types.SpellCorrectionSpec.Types.Mode.Auto
                    },
                    ContentSearchSpec = new SearchRequest.Types.ContentSearchSpec
                    {
                        ExtractiveContentSpec = ecs,
                        SnippetSpec = new() { ReturnSnippet = true }
                    }
                };

                var results = _client.SearchAsync(req);
                var list = new List<ContextChunk>();

                await foreach (var item in results.WithCancellation(ct))
                {
                    var doc = item.Document;
                    string? source = doc?.Name ?? doc?.Id;

                    // 1) extractive answer
                    if (doc?.DerivedStructData?.Fields?.TryGetValue("extractive_answer", out var ans) == true)
                    {
                        var t = ans.StringValue;
                        if (!string.IsNullOrWhiteSpace(t))
                            list.Add(new ContextChunk(t, source, doc?.Id, null));
                    }

                    // 2) extractive segments
                    if (doc?.DerivedStructData?.Fields?.TryGetValue("extractive_segments", out var segs) == true)
                    {
                        foreach (var v in segs.ListValue.Values)
                        {
                            if (v.StructValue.Fields.TryGetValue("content", out var contentField))
                            {
                                var t = contentField.StringValue;
                                if (!string.IsNullOrWhiteSpace(t))
                                    list.Add(new ContextChunk(t, source, doc?.Id, null));
                            }
                        }
                    }

                    // 3) snippets (limpios)
                    if (doc?.DerivedStructData?.Fields?.TryGetValue("snippets", out var sn) == true)
                    {
                        foreach (var v in sn.ListValue.Values)
                        {
                            if (v.StructValue.Fields.TryGetValue("snippet", out var sf))
                            {
                                var raw = sf.StringValue;
                                if (!string.IsNullOrWhiteSpace(raw))
                                {
                                    var clean = Regex.Replace(raw, "<.*?>", string.Empty);
                                    list.Add(new ContextChunk(clean, source, doc?.Id, null));
                                }
                            }
                        }
                    }

                    // 4) Fallback
                    if (doc?.StructData?.Fields != null && doc.StructData.Fields.TryGetValue("content", out var c))
                        list.Add(new ContextChunk(c.StringValue, source, doc?.Id, null));
                }

                var distinct = list
                    .GroupBy(x => x.Text)
                    .Select(g => g.First())
                    .Take(_maxSeg + _maxAns)
                    .ToList();

                _logger.LogInformation("Retrieve devolvió {Count} chunks para query='{Query}'", distinct.Count, query);
                return distinct;
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Retrieve cancelado para query='{Query}'", query);
                return Array.Empty<ContextChunk>();
            }
            catch (Grpc.Core.RpcException ex)
            {
                _logger.LogError(ex, "gRPC error en Search (StatusCode={Code}) para query='{Query}'", ex.StatusCode, query);
                return Array.Empty<ContextChunk>();
            }
            catch (Google.GoogleApiException ex)
            {
                _logger.LogError(ex, "Google API error (HttpStatusCode={Code}) en Search para query='{Query}'", ex.HttpStatusCode, query);
                return Array.Empty<ContextChunk>();
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "HTTP error al llamar Discovery Engine para query='{Query}'", query);
                return Array.Empty<ContextChunk>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error inesperado en Retrieve para query='{Query}'", query);
                return Array.Empty<ContextChunk>();
            }
        }

    }
}
