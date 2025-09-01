namespace BotApp.Models
{
    public class EventLog
    {
        public long Id { get; set; }
        public string Type { get; set; } = default!; // QnA / CrearDenuncia / ConsultarExpediente / Handoff / Error
        public Guid? SessionId { get; set; }
        public string? PayloadJson { get; set; }
        public int? ElapsedMs { get; set; }
        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    }
}
