using BotApp.Models;
using Google.Cloud.AIPlatform.V1;
using System.Text;



namespace BotApp.Services
{
    public interface IGeminiService
    {
        Task<string> AnswerAsync(string question, IEnumerable<ContextChunk> context, CancellationToken ct = default);
    }
    public sealed class GeminiService : IGeminiService
    {
        private readonly string _model;
        private readonly int _maxContextChars;

        public GeminiService(IConfiguration cfg)
        {
            _model = cfg["GoogleCloud:Gemini:Model"]
                ?? "projects/rag-bot-dhr/locations/us-central1/publishers/google/models/gemini-2.5-flash-lite";
            _maxContextChars = int.TryParse(cfg["GoogleCloud:Gemini:MaxContextChars"], out var m) ? m : 12000;
        }

        public async Task<string> AnswerAsync(string question, IEnumerable<ContextChunk> context, CancellationToken ct = default)
        {
            var client = await PredictionServiceClient.CreateAsync();

            // Compactar contexto (evitar prompts gigantes)
            var sb = new StringBuilder();
            foreach (var c in context)
            {
                var block = $"- {c.Text}\n";
                if (sb.Length + block.Length > _maxContextChars) break;
                sb.Append(block);
            }

            var system = new Content
            {
                Role = "system",
                Parts = { new Part { Text = "Responde claro y directo en español. Usa solo la evidencia del contexto. Si no hay evidencia suficiente, dilo brevemente." } }
            };

            var user = new Content
            {
                Role = "user",
                Parts = { new Part { Text = $"Contexto:\n{sb}\n\nPregunta: {question}" } }
            };

            var req = new GenerateContentRequest
            {
                Model = _model,
                SystemInstruction = system,
                Contents = { user }
            };

            var resp = await client.GenerateContentAsync(req, cancellationToken: ct);
            return resp.Candidates.FirstOrDefault()?.Content.Parts.FirstOrDefault()?.Text
                   ?? "No encontré información suficiente en los documentos para responder con certeza.";
        }
    }
}
