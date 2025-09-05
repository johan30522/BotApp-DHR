using BotApp.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace BotApp.Controllers
{
    [Route("expedientes")]
    [ApiController]
    public class ExpedientesController : ControllerBase
    {
        private readonly ExpedientesService _svc;

        public ExpedientesController(ExpedientesService svc) => _svc = svc;

        // GET /expedientes?numero=EXP-2025-0001
        [HttpGet]
        public async Task<IActionResult> Get([FromQuery] string? numero, [FromQuery] string? cedula, CancellationToken ct)
        {
            if (!string.IsNullOrWhiteSpace(numero))
            {
                var e = await _svc.GetByNumeroAsync(numero, ct);
                return e is null ? NotFound() : Ok(e);
            }

            //  habilitar búsqueda por cédula, necesitamos agregar campo Cedula a la entidad + migración.
            if (!string.IsNullOrWhiteSpace(cedula))
            {
                return BadRequest("Búsqueda por cédula no disponible. Agregar campo Cedula a Expediente y migración.");
            }

            return BadRequest("Debe enviar query param ?numero=...");
        }
    }
}
