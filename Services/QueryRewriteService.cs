using BotApp.Models;

namespace BotApp.Services
{
    public interface IQueryRewriteService
    {
        Task<string?> RewriteAsync(string question, IEnumerable<QnaExchange> recent, CancellationToken ct);
    }

    public sealed class QueryRewriteService : IQueryRewriteService
    {
        private readonly IGeminiService _gemini;
        public QueryRewriteService(IGeminiService gemini) => _gemini = gemini;

        public async Task<string?> RewriteAsync(string question, IEnumerable<QnaExchange> recent, CancellationToken ct)
        {
            // Si no hay historia, no hay nada que reescribir
            var hist = recent?.ToList() ?? new();
            if (hist.Count == 0) return null;

            // Construimos un prompt MUY corto y preciso
            var mini = string.Join("\n", hist.Take(2).Select((e, i) => $"(Q{i + 1}) {e.q}\n(A{i + 1}) {e.a}"));
            var ask = $"Reescribe la siguiente consulta del usuario en una pregunta completa y auto-contenida " +
                      $"usando solo el contexto reciente. No inventes datos. " +
                      $"Si falta información, deja la parte faltante como '___'. " +
                      $"Contexto reciente:\n{mini}\n\nConsulta original: {question}\n\nConsulta reescrita:";

            // Reutilizamos IGeminiService, pasándole el 'ask' como pregunta y sin contexto extra
            var rewritten = await _gemini.AnswerAsync(ask, Array.Empty<ContextChunk>(), ct);
            // Limpieza básica
            var clean = rewritten?.Trim().Trim('"').Trim();
            return string.IsNullOrWhiteSpace(clean) ? null : clean;
        }
    }
}
