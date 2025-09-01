namespace BotApp.Models
{
    public class Expediente
    {
        public string Numero { get; set; } = default!;
        public string Estado { get; set; } = default!;
        public string? DatosJson { get; set; }
        public DateTime LastModifiedUtc { get; set; } = DateTime.UtcNow;
    }
}
