namespace BotApp.Models
{
    public class Denuncia
    {
        public long Id { get; set; }
        public Guid SessionId { get; set; }
        public string Estado { get; set; } = "CREADA";
        public string? DatosJson { get; set; } // campos dinámicos opcionales
        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    }
}
