using BotApp.Data;
using BotApp.DTO.Expedientes;
using BotApp.Models;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace BotApp.Services
{
    public class ExpedientesService
    {
        private readonly BotDbContext _db;
        private static readonly JsonSerializerOptions JsonOpts = new(JsonSerializerDefaults.Web);

        public ExpedientesService(BotDbContext db) => _db = db;

        public async Task<ExpedienteResponse?> GetByNumeroAsync(string numero, CancellationToken ct = default)
        {
            var e = await _db.Expedientes.FirstOrDefaultAsync(x => x.Numero == numero, ct);
            if (e == null) return null;

            // Intentar extraer un “resumen” de DatosJson si lo tenés
            string? resumen = null;
            if (!string.IsNullOrWhiteSpace(e.DatosJson))
            {
                try
                {
                    var doc = JsonDocument.Parse(e.DatosJson);
                    if (doc.RootElement.TryGetProperty("Resumen", out var resEl))
                        resumen = resEl.GetString();
                }
                catch { /* dejar resumen = null */ }
            }

            return new ExpedienteResponse
            {
                Numero = e.Numero,
                Estado = e.Estado,
                Resumen = resumen,
                LastModifiedUtc = e.LastModifiedUtc
            };
        }

        // Útil para Fase F (sync): crear/actualizar expedientes
        public async Task UpsertAsync(string numero, string estado, object? datos, DateTime? lastModifiedUtc = null, CancellationToken ct = default)
        {
            var e = await _db.Expedientes.FirstOrDefaultAsync(x => x.Numero == numero, ct);
            var datosJson = datos != null ? JsonSerializer.Serialize(datos, JsonOpts) : null;

            if (e == null)
            {
                e = new Expediente
                {
                    Numero = numero,
                    Estado = estado,
                    DatosJson = datosJson,
                    LastModifiedUtc = lastModifiedUtc ?? DateTime.UtcNow
                };
                _db.Expedientes.Add(e);
            }
            else
            {
                e.Estado = estado;
                e.DatosJson = datosJson ?? e.DatosJson;
                e.LastModifiedUtc = lastModifiedUtc ?? DateTime.UtcNow;
            }
            await _db.SaveChangesAsync(ct);
        }
    }
}
