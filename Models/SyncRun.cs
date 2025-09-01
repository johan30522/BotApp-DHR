namespace BotApp.Models
{
    public class SyncRun
    {
        public long Id { get; set; }
        public string Direction { get; set; } = default!; // up/down
        public DateTime StartedAtUtc { get; set; } = DateTime.UtcNow;
        public DateTime? EndedAtUtc { get; set; }
        public int Inserted { get; set; }
        public int Updated { get; set; }
        public int Errors { get; set; }
    }
}
