using Google.Api.Gax;
using Google.Api.Gax.Grpc; // CallSettings, Expiration
using Google.Apis.Auth.OAuth2;
using Google.Cloud.AIPlatform.V1;
using Google.Protobuf;                 // <- para parseo binario opcional
using Google.Rpc;
using Grpc.Core;                       // <- RpcException
using Grpc.Core;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using RpcStatus = Google.Rpc.Status;// <- para ErrorInfo opcional (requiere Google.Api.CommonProtos)


namespace BotApp.Services
{
    public class GeminiRagService
    {
        // 👇 limita concurrencia interna del servicio (ajusta 3–8 según tu carga)
        private static readonly SemaphoreSlim _gate = new(5);

        private readonly string _projectId;
        private readonly string _location;
        private readonly string _model;
        private readonly string _corpusId;
        private readonly int _topK;
        private readonly int _maxOutputTokens;
        private readonly float _temperature;
        private readonly bool _appendLink;
        private readonly string _moreInfoLink;
        private readonly string _systemInstruction;

        private readonly ILogger<GeminiRagService> _logger;
        private readonly PredictionServiceClient _client;

        public GeminiRagService(IConfiguration cfg, ILogger<GeminiRagService> logger, PredictionServiceClient client)
        {
            _logger = logger;
            var gcp = cfg.GetSection("Gcp");
            _projectId = gcp["ProjectId"]!;
            _location = gcp["Location"] ?? "us-east4";
            _model = gcp["Model"] ?? "gemini-2.5-flash-001";
            _corpusId = gcp["RagCorpusId"]!;
            _topK = int.TryParse(gcp["TopK"], out var k) ? k : 3;
            _maxOutputTokens = int.TryParse(gcp["MaxOutputTokens"], out var t) ? t : 500;
            _temperature = float.TryParse(gcp["Temperature"], out var temp) ? temp : 0.25f;
            _appendLink = bool.TryParse(gcp["AppendLink"], out var ap) && ap;
            _moreInfoLink = gcp["MoreInfoLink"] ?? "https://www.dhr.go.cr/";
            _systemInstruction = gcp["SystemInstruction"] ?? string.Empty;
            _client = client;
        }

        /// <summary>
        /// PATRÓN SIMPLE (1-paso): el modelo llama al RAG como tool.
        /// No requiere REST manual de retrieveContexts y evita tipos no soportados (p. ej. RagRetrieveConfig).
        /// </summary>
        public async Task<string> AskGeminiAsync(string question, CancellationToken ct = default)
        {
            // ID de diagnóstico para correlacionar logs
            var diagId = Guid.NewGuid().ToString("N");
            _logger.LogInformation("AskGeminiAsync start diagId={DiagId} proj={ProjectId} loc={Loc} model={Model} corpusId={Corpus}",
                diagId, _projectId, _location, _model, _corpusId);

            try
            {
                // Cliente gRPC (usa ADC)
               // var client = await PredictionServiceClient.CreateAsync(cancellationToken: ct);

                var modelPath = $"projects/{_projectId}/locations/{_location}/publishers/google/models/{_model}";
                var corpusPath = $"projects/{_projectId}/locations/{_location}/ragCorpora/{_corpusId}";

                // Instrucción del sistema (tono ciudadano)
                var systemInstruction = new Content
                {
                    Role = "system",
                    Parts = { new Part { Text = _systemInstruction } }
                };

                // Tool RAG apuntando a tu corpus
                var tools = new List<Tool>
            {
                new Tool
                {
                    Retrieval = new Retrieval
                    {
                        VertexRagStore = new VertexRagStore
                        {
                            RagResources =
                            {
                                new VertexRagStore.Types.RagResource { RagCorpus = corpusPath }
                            }
                        }
                        // NOTA: no hay RagRetrieveConfig en este SDK.
                        // El motor usará valores por defecto razonables (puedes controlar TopK en el patrón 2-pasos).
                    }
                }
            };

                var request = new GenerateContentRequest
                {
                    Model = modelPath,
                    SystemInstruction = systemInstruction,
                    Tools = { tools },
                    Contents =
                        {
                            new Content { Role = "user", Parts = { new Part { Text = question } } }
                        },
                    GenerationConfig = new GenerationConfig
                    {
                        MaxOutputTokens = _maxOutputTokens,
                        Temperature = _temperature
                        // Si quieres aún más determinismo: TopP = 0.8f;
                    }
                };
                var callSettings = CallSettings
                    .FromExpiration(Expiration.FromTimeout(TimeSpan.FromSeconds(12)))
                    .WithCancellationToken(ct);

                // 🔒 limita concurrencia + 🔁 retry con backoff y jitter
                await _gate.WaitAsync(ct);
                GenerateContentResponse response;
                try
                {
                    int[] delaysMs = { 250, 500, 1000, 2000 }; // backoff exponencial base
                    for (int attempt = 0; ; attempt++)
                    {
                        try
                        {
                            response = await _client.GenerateContentAsync(request, callSettings);
                            break; // ✅ OK
                        }
                        catch (RpcException ex) when (
                            ex.StatusCode == StatusCode.ResourceExhausted ||   // 429
                            ex.StatusCode == StatusCode.Unavailable ||         // 503
                            ex.StatusCode == StatusCode.DeadlineExceeded)      // 504
                        {
                            if (attempt >= delaysMs.Length - 1) throw; // último intento: re-lanza
                            var delay = TimeSpan.FromMilliseconds(delaysMs[attempt] + Random.Shared.Next(50, 150));
                            _logger.LogWarning("Retry {Attempt}/{Max} after {Delay} due to {Code} diagId={DiagId}",
                                attempt + 1, delaysMs.Length, delay, ex.StatusCode, diagId);
                            await Task.Delay(delay, ct);
                        }
                    }
                }
                finally
                {
                    _gate.Release();
                }

                var text = response.Candidates.FirstOrDefault()
                                ?.Content?.Parts.FirstOrDefault()
                                ?.Text ?? "(sin respuesta)";

                if (_appendLink && !text.Contains("Más información:", StringComparison.OrdinalIgnoreCase))
                    text = text.Trim() + $"\n\nMás información: {_moreInfoLink}";

                return text;
            }
            catch (OperationCanceledException oce)
            {
                _logger.LogWarning(oce, "AskGeminiAsync canceled/timeout diagId={DiagId}", diagId);
                return "Estoy tardando más de lo normal. Por favor, intente de nuevo.";
            }
            catch (RpcException rpcEx)
            {
                LogGrpcError(rpcEx, diagId, "AskGeminiAsync");
                return "Tuvimos un inconveniente al consultar la información. Por favor, inténtelo de nuevo.";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "AskGeminiAsync unexpected error diagId={DiagId}", diagId);
                return "Ocurrió un error procesando su solicitud. Por favor, intente nuevamente.";
            }
        }

        /// <summary>
        /// OPCIONAL: PATRÓN 2-PASOS para controlar 'topK' explícitamente:
        /// 1) retrieveContexts (REST)  2) generateContent (gRPC) con los trozos.
        /// Úsalo si necesitas citas/scores/filtrado fino más adelante.
        /// </summary>
        public async Task<string> AskTwoStepAsync(string question, CancellationToken ct = default)
        {
            var chunks = await RetrieveContextsAsync(question, _topK, ct);
            var ctxText = chunks.Count == 0
                ? "Contexto: (no se encontraron pasajes relevantes)"
                : "Contexto (no inventar):\n- " + string.Join("\n- ", chunks);

            var client = await PredictionServiceClient.CreateAsync(cancellationToken: ct);
            var modelPath = $"projects/{_projectId}/locations/{_location}/publishers/google/models/{_model}";

            var system = new Content { Role = "system", Parts = { new Part { Text = _systemInstruction } } };

            var request = new GenerateContentRequest
            {
                Model = modelPath,
                SystemInstruction = system,
                Contents =
            {
                new Content { Role = "user", Parts = { new Part { Text = question } } },
                new Content { Role = "user", Parts = { new Part { Text = ctxText } } }
            },
                GenerationConfig = new GenerationConfig
                {
                    MaxOutputTokens = _maxOutputTokens,
                    Temperature = _temperature
                }
            };

            var response = await client.GenerateContentAsync(request, cancellationToken: ct);
            var text = response.Candidates.FirstOrDefault()
                            ?.Content?.Parts.FirstOrDefault()
                            ?.Text ?? "(sin respuesta)";

            if (_appendLink && !text.Contains("Más información:", StringComparison.OrdinalIgnoreCase))
                text = text.Trim() + $"\n\nMás información: {_moreInfoLink}";

            return text;
        }

        // ===== Helper REST para retrieveContexts (us-east4) =====
        private async Task<List<string>> RetrieveContextsAsync(string question, int topK, CancellationToken ct)
        {
            // TOKEN vía ADC
            var cred = await GoogleCredential.GetApplicationDefaultAsync();
            var scoped = cred.CreateScoped("https://www.googleapis.com/auth/cloud-platform");
            var token = await scoped.UnderlyingCredential.GetAccessTokenForRequestAsync();

            using var http = new HttpClient();
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var url = $"https://{_location}-aiplatform.googleapis.com/v1/projects/{_projectId}/locations/{_location}:retrieveContexts";
            var body = new
            {
                query = new { text = question, ragRetrievalConfig = new { topK } },
                vertexRagStore = new
                {
                    ragResources = new[] { new { ragCorpus = $"projects/{_projectId}/locations/{_location}/ragCorpora/{_corpusId}" } }
                }
            };

            var json = JsonSerializer.Serialize(body);
            var res = await http.PostAsync(url, new StringContent(json, Encoding.UTF8, "application/json"), ct);
            res.EnsureSuccessStatusCode();

            using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync(ct));
            var list = new List<string>();
            var hasCtxObj = doc.RootElement.TryGetProperty("contexts", out var ctxObj);
            if (hasCtxObj && ctxObj.TryGetProperty("contexts", out var arr))
            {
                foreach (var c in arr.EnumerateArray())
                {
                    if (c.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String)
                        list.Add(t.GetString()!);
                }
            }
            return list;
        }
        // ===== Util: logging gRPC con trailers/Status =====
        private void LogGrpcError(RpcException rpcEx, string diagId, string op)
        {
            try
            {
                var code = rpcEx.StatusCode;
                var detail = rpcEx.Status.Detail ?? "(sin detalle)";
                _logger.LogError(rpcEx, "{Op} gRPC error diagId={DiagId} status={Status} detail={Detail}",
                    op, diagId, code, detail);

                // Trailers crudos
                if (rpcEx.Trailers != null && rpcEx.Trailers.Count > 0)
                {
                    foreach (var tr in rpcEx.Trailers)
                        _logger.LogError("{Op} trailer diagId={DiagId} {Key}={Value}", op, diagId, tr.Key, tr.Value);

                    // Intento de extraer google.rpc.Status -> ErrorInfo
                    var entry = rpcEx.Trailers.Get("grpc-status-details-bin");
                    if (entry != null)
                    {
                        // Nota: ValueBytes requiere Grpc.Core >= 2.x; si no compila, comenta 3 líneas abajo.
                        var bytes = rpcEx.Trailers.GetValueBytes("grpc-status-details-bin");
                        if (bytes != null)
                        {
                            var st = RpcStatus.Parser.ParseFrom(bytes);
                            foreach (var any in st.Details)
                            {
                                if (any.Is(ErrorInfo.Descriptor))
                                {
                                    var info = any.Unpack<ErrorInfo>();
                                    _logger.LogError("{Op} ErrorInfo diagId={DiagId} domain={Domain} reason={Reason} metadata={Meta}",
                                        op, diagId, info.Domain, info.Reason,
                                        string.Join(",", info.Metadata.Select(kv => $"{kv.Key}={kv.Value}")));
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception e)
            {
                _logger.LogWarning(e, "LogGrpcError failed diagId={DiagId}", diagId);
            }
        }
    }
}
