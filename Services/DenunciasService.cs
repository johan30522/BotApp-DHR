using BotApp.Data;
using BotApp.DTO.Denuncias;
using BotApp.Models;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace BotApp.Services
{
    public class DenunciasService
    {
        private readonly BotDbContext _db;
        private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);


        public DenunciasService(BotDbContext db) => _db = db;


        public async Task<DenunciaResponse> CreateAsync(CreateDenunciaDto dto, CancellationToken ct = default)
        {
            // Validar sesión
            var session = await _db.Sessions.FirstOrDefaultAsync(s => s.Id == dto.SessionId, ct);
            if (session == null) throw new InvalidOperationException("Session not found.");

            // Armar DatosJson
            var datos = new Dictionary<string, object?>
            {
                ["Nombre"] = dto.Nombre,
                ["Cedula"] = dto.Cedula,
                ["Ubicacion"] = dto.Ubicacion,
                ["Descripcion"] = dto.Descripcion
            };
            if (dto.Extras != null)
            {
                foreach (var kv in dto.Extras) datos[kv.Key] = kv.Value;
            }

            var entity = new Denuncia
            {
                SessionId = dto.SessionId,
                Estado = "CREADA",
                DatosJson = JsonSerializer.Serialize(datos, JsonOpts),
                CreatedAtUtc = DateTime.UtcNow
            };

            _db.Denuncias.Add(entity);
            await _db.SaveChangesAsync(ct);

            return new DenunciaResponse
            {
                Id = entity.Id,
                SessionId = entity.SessionId,
                Estado = entity.Estado,
                CreatedAtUtc = entity.CreatedAtUtc,
                Nombre = dto.Nombre,
                Cedula = dto.Cedula,
                Ubicacion = dto.Ubicacion,
                Descripcion = dto.Descripcion
            };
        }

        public async Task<Denuncia?> GetByIdAsync(long id, CancellationToken ct = default)
            => await _db.Denuncias.FirstOrDefaultAsync(d => d.Id == id, ct);

    }
}
