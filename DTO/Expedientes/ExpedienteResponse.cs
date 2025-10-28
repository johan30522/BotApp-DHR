namespace BotApp.DTO.Expedientes
{
    public class ExpedienteResponse
    {
        public string Numero { get; set; } = default!;
        public string Estado { get; set; } = default!;
        public string? Email { get; set; } = default!;
        public string? Resumen { get; set; }
        public DateTime LastModifiedUtc { get; set; }
    }
}
