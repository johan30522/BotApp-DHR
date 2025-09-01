namespace BotApp.Models
{
    public class Message
    {
        public long Id { get; set; }
        public Guid SessionId { get; set; }
        public string Direction { get; set; } = default!; // in/out
        public string? Text { get; set; }
        public string? PayloadJson { get; set; }
        public string? ChannelMessageId { get; set; }
        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    }
}
