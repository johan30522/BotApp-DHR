

namespace BotApp.DTO.Denuncias
{

    public class CreateDenunciaDto
    {
        public Guid SessionId { get; set; }

        // Datos “visibles” que serializaremos dentro de DatosJson
        public string Nombre { get; set; } = default!;
        public string Cedula { get; set; } = default!;
        public string Ubicacion { get; set; } = default!;
        public string Descripcion { get; set; } = default!;

        // Si querés pasar extras, opcional:
        public Dictionary<string, object>? Extras { get; set; }
    }
}
