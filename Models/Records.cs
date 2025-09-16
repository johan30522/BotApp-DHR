namespace BotApp.Models
{
    public record ContextChunk(string Text, string? SourceUri, string? DocumentId, float? Score);
    public record AskResponse(string Question, string Answer, IReadOnlyList<ContextChunk> Context);
}
