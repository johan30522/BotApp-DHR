using System.Text.Json.Serialization;

namespace BotApp.Contracts
{
    public abstract record SseBaseEvent(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("sessionId")] string SessionId,
        [property: JsonPropertyName("turnId")] string TurnId,
        [property: JsonPropertyName("ts")] DateTime Ts
    );

    public sealed record SseMessageEvent(
        string SessionId,
        string TurnId,
        string Role,               // "assistant" | "user" | "system"
        string Phase,              // "ack" | "progress" | "final"
        string Text,
        object? Meta,
        DateTime Ts
    ) : SseBaseEvent("message", SessionId, TurnId, Ts);

    public sealed record SseToolEvent(
        string SessionId,
        string TurnId,
        string Tool,               // "discovery_search", "gemini", "db", etc.
        string Status,             // "start" | "progress" | "end"
        object? Progress,          // e.g. new { current = 2, total = 5 }
        DateTime Ts
    ) : SseBaseEvent("tool_call", SessionId, TurnId, Ts);

    public sealed record SseDoneEvent(
        string SessionId,
        string TurnId,
        DateTime Ts
    ) : SseBaseEvent("done", SessionId, TurnId, Ts);

    public sealed record SseErrorEvent(
        string SessionId,
        string TurnId,
        string Code,               // "RAG_TIMEOUT", "OTP_INVALID", etc.
        string Message,
        bool Retryable,
        DateTime Ts
    ) : SseBaseEvent("error", SessionId, TurnId, Ts);
}
