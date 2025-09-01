namespace BotApp.Models
{
    public class SyncRunError
    {
        public long Id { get; set; }
        public long SyncRunId { get; set; }
        public string ItemKey { get; set; } = default!;
        public string Error { get; set; } = default!;
        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    }
}
