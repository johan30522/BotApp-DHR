namespace BotApp.Services
{
    public interface IConversationRagService
    {
        Task<string> AskAsync(Guid sessionId, string question, CancellationToken ct = default);
    }
    public sealed class ConversationRagService : IConversationRagService
    {
        private readonly ISearchService _search;
        private readonly IGeminiService _gemini;
        private readonly SessionStateStore _state;
        private readonly ILogger<ConversationRagService> _logger;
        private readonly IQueryRewriteService _rewrite;

        // afinables
        private const int HISTORY_TAKE = 2; // cuántos QnA recientes metemos al prompt

        public ConversationRagService(
            ISearchService search,
            IGeminiService gemini,
            SessionStateStore state,
            IQueryRewriteService rewrite,
            ILogger<ConversationRagService> logger)
        {
            _search = search;
            _gemini = gemini;
            _state = state;
            _logger = logger;
            _rewrite = rewrite;
        }

        public async Task<string> AskAsync(Guid sessionId, string question, CancellationToken ct = default)
        {
            var recent = await _state.GetRecentQnaAsync(sessionId, HISTORY_TAKE);

            // 1) Decide si es repregunta
            var followUp = LooksLikeFollowUp(question);

            // 2) Si es repregunta, reescribe; si no, usa tal cual
            string qForSearch = question;
            if (followUp && recent.Count > 0)
            {
                var rw = await _rewrite.RewriteAsync(question, recent, ct);
                if (!string.IsNullOrWhiteSpace(rw))
                {
                    qForSearch = rw;
                    _logger.LogInformation("Query reescrita: '{Original}' -> '{Rewrite}'", question, qForSearch);
                }
            }

            // 3) Retrieval con Discovery
            var ctx = await _search.RetrieveAsync(qForSearch, ct);

            // 4) Construir prompt final para Gemini (historia breve + fragmentos)
            var history = string.Join("\n\n", recent.Select((e, i) => $"(Q{i + 1}) {e.q}\n(A{i + 1}) {e.a}"));
            var prompt = string.IsNullOrWhiteSpace(history)
                ? question
                : $"Contexto reciente de la conversación:\n{history}\n\nNueva pregunta: {question}";

            var answer = await _gemini.AnswerAsync(prompt, ctx, ct);

            await _state.AddQnaAsync(sessionId, new QnaExchange { q = question, a = answer });
            return answer;
        }
        private static bool LooksLikeFollowUp(string q)
        {
            if (string.IsNullOrWhiteSpace(q)) return false;
            var t = q.Trim().ToLowerInvariant();
            // misma idea que ya usabas, ampliamos un pelín:
            string[] markers = { "eso", "lo anterior", "ahí", "alli", "entonces", "ok", "y si", "según", "como hago", "¿y", "y qué" };
            return t.Length <= 80 || markers.Any(m => t.Contains(m));
        }
    }
}
