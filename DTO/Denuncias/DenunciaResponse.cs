
namespace BotApp.DTO.Denuncias
{

    public class DenunciaResponse
    {
        public long Id { get; set; }
        public Guid SessionId { get; set; }
        public string Estado { get; set; } = default!;
        public DateTime CreatedAtUtc { get; set; }

        // Eco de datos “de negocio”
        public string? Nombre { get; set; }
        public string? Cedula { get; set; }
        public string? Ubicacion { get; set; }
        public string? Descripcion { get; set; }
    }
}
