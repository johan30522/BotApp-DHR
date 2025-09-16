using Google.Cloud.Dialogflow.Cx.V3;
using Google.Protobuf.WellKnownTypes;
namespace BotApp.Services
{
    public class CxDetectService
    {

        private readonly SessionsClient _sessions;
        private readonly string _agentPath;      // projects/.../locations/.../agents/...
        private readonly string? _environmentId; // null/empty => Draft

        public CxDetectService(IConfiguration cfg, SessionsClient sessions)
        {
            _sessions = sessions;
            _agentPath = cfg["Cx:AgentPath"] ?? throw new("Cx:AgentPath missing");
            _environmentId = cfg["Cx:EnvironmentId"]; // opcional
        }

        private string BuildSessionPath(string sessionId) =>
            string.IsNullOrWhiteSpace(_environmentId)
                ? $"{_agentPath}/sessions/{sessionId}"                                // Draft
                : $"{_agentPath}/environments/{_environmentId}/sessions/{sessionId}"; // Environment

        public async Task<DetectIntentResponse> DetectAsync(
            string sessionId,
            string text,
            IDictionary<string, object>? paramsToMerge = null,
            CancellationToken ct = default)
        {
            var req = new DetectIntentRequest
            {
                Session = BuildSessionPath(sessionId),
                QueryInput = new QueryInput
                {
                    Text = new TextInput { Text = text ?? string.Empty },
                    LanguageCode = "es"
                },
                QueryParams = new QueryParameters
                {
                    Parameters = paramsToMerge is null || paramsToMerge.Count == 0
                        ? new Struct()
                        : Struct.Parser.ParseJson(System.Text.Json.JsonSerializer.Serialize(paramsToMerge))
                }
            };

            return await _sessions.DetectIntentAsync(req, cancellationToken: ct);
        }
    }
}
