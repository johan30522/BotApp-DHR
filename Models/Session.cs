namespace BotApp.Models
{
    public class Session
    {
        public Guid Id { get; set; }
        public string Channel { get; set; } = default!;
        public string ChannelUserId { get; set; } = default!;
        public string CxSessionPath { get; set; } = default!;
        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
        public DateTime LastActivityUtc { get; set; } = DateTime.UtcNow;
        public string? State { get; set; } // JSON pequeño con flags 
    }
}
