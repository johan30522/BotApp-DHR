namespace BotApp.Models
{
    public record UniAttachment(string Name, string Type, long? SizeBytes, string? Url);

    public record UniMessage(
        string IdempotencyKey,
        string Channel,            // "web" | "whatsapp" | ...
        string ChannelUserId,
        string? ThreadId,
        string? Text,
        List<UniAttachment>? Attachments,
        Dictionary<string, string>? Meta
    );
}
