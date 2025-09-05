using BotApp.DTO.Denuncias;
using BotApp.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace BotApp.Controllers
{
    [Route("denuncias")]
    [ApiController]
    public class DenunciasController : ControllerBase
    {
        private readonly DenunciasService _svc;

        public DenunciasController(DenunciasService svc) => _svc = svc;

        [HttpPost]
        public async Task<IActionResult> Create([FromBody] CreateDenunciaDto dto, CancellationToken ct)
        {
            if (dto.SessionId == Guid.Empty) return BadRequest("SessionId requerido.");
            if (string.IsNullOrWhiteSpace(dto.Nombre)) return BadRequest("Nombre requerido.");
            if (string.IsNullOrWhiteSpace(dto.Cedula)) return BadRequest("Cedula requerida.");
            if (string.IsNullOrWhiteSpace(dto.Ubicacion)) return BadRequest("Ubicacion requerida.");
            if (string.IsNullOrWhiteSpace(dto.Descripcion)) return BadRequest("Descripcion requerida.");

            try
            {
                var resp = await _svc.CreateAsync(dto, ct);
                return CreatedAtAction(nameof(GetById), new { id = resp.Id }, resp);
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("Session not found"))
            {
                return NotFound("Session no existe.");
            }
        }

        [HttpGet("{id:long}")]
        public async Task<IActionResult> GetById([FromRoute] long id, CancellationToken ct)
        {
            var d = await _svc.GetByIdAsync(id, ct);
            if (d == null) return NotFound();
            return Ok(new
            {
                d.Id,
                d.SessionId,
                d.Estado,
                d.DatosJson,
                d.CreatedAtUtc
            });
        }
    }
}
