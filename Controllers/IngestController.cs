using BotApp.Filters;
using BotApp.Infrastructure.Logging;
using BotApp.Models;
using BotApp.Services;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using System.Text.Json;

namespace BotApp.Controllers
{
    [Route("ingest")]
    [ApiController]
    public class IngestController : ControllerBase
    {
        private readonly SessionService _sessions;
        private readonly ILogger<IngestController> _logger;
        private readonly TokenService _tokens;

        public IngestController(SessionService sessions, TokenService tokens, ILogger<IngestController> logger)
        {
            _tokens = tokens;
            _sessions = sessions;
            _logger = logger;
        }



        [HttpPost("web")]
        [AllowAnonymous]
        public async Task<IActionResult> Web([FromBody] UniMessage msg, CancellationToken ct)
        {
            // ¿Viene Authorization? Intentamos autenticar con el esquema JWT
            var auth = await HttpContext.AuthenticateAsync(JwtBearerDefaults.AuthenticationScheme);

            if (auth.Succeeded && auth.Principal?.Identity?.IsAuthenticated == true)
            {
                // ----- MODO MENSAJE (autenticado) -----
                var user = auth.Principal;
                var claimChannel = user.FindFirst("channel")?.Value;
                var claimSub = user.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? user.FindFirst("sub")?.Value;
                var claimSid = user.FindFirst("sid")?.Value;

                if (!string.Equals(claimChannel, "web", StringComparison.OrdinalIgnoreCase))
                    return Unauthorized("Token inválido: canal incorrecto.");
                if (!string.Equals(claimSub, msg.ChannelUserId, StringComparison.Ordinal))
                    return Unauthorized("Token inválido: usuario no corresponde.");

                // Usamos sessionId del token si existe; si no, GetOrCreate por userId
                Guid sessionId = Guid.TryParse(claimSid, out var sid) ? sid : Guid.Empty;
                BotApp.Models.Session session;
                if (sessionId != Guid.Empty)
                {
                    session = await _sessions.GetOrCreateAsync("web", msg.ChannelUserId, null, ct);
                    if (session.Id != sessionId)
                    {
                        // edge-case: si no coincide, nos quedamos con la persistida
                        sessionId = session.Id;
                    }
                }
                else
                {
                    session = await _sessions.GetOrCreateAsync("web", msg.ChannelUserId, null, ct);
                    sessionId = session.Id;
                }

                var payload = msg.Attachments is { Count: > 0 } || (msg.Meta is { Count: > 0 })
                    ? JsonSerializer.Serialize(new { msg.Attachments, msg.Meta })
                    : null;

                await _sessions.AddIncomingMessageAsync(sessionId, msg.Text, payload, msg.ThreadId, ct);

                _logger.LogInformation("IN web/{user}: {text}", msg.ChannelUserId, PiiRedactor.Safe(msg.Text));

                await _sessions.AddEventAsync(sessionId, type: "Ingest", result: "ok", dataJson: null, ct: ct);

                return Ok(new
                {
                    accepted = true,
                    mode = "message",
                    channel = "web",
                    sessionId = sessionId,
                    cxSessionPath = session.CxSessionPath
                });
            }
            else
            {
                // ----- MODO BOOTSTRAP (anónimo) -----
                if (string.IsNullOrWhiteSpace(msg.ChannelUserId))
                    return BadRequest("ChannelUserId requerido para bootstrap.");

                var session = await _sessions.GetOrCreateAsync("web", msg.ChannelUserId, null, ct);
                var token = _tokens.IssueWebToken(session.Id, msg.ChannelUserId);

                await _sessions.AddEventAsync(session.Id, type: "Bootstrap", result: "ok", dataJson: null, ct: ct);

                return Ok(new
                {
                    accepted = true,
                    mode = "bootstrap",
                    channel = "web",
                    sessionId = session.Id,
                    token
                });
            }
        }

        [HttpPost("whatsapp")]
        [ServiceFilter(typeof(MetaSignatureFilter))]
        public async Task<IActionResult> WhatsApp([FromBody] UniMessage msg, CancellationToken ct)
        {
            return await HandleIngest(msg with { Channel = "whatsapp" }, ct);
        }


        private async Task<IActionResult> HandleIngest(UniMessage msg, CancellationToken ct)
        {
            // 1) Sesión (channel + userId)
            var session = await _sessions.GetOrCreateAsync(msg.Channel, msg.ChannelUserId, null, ct);

            // 2) Registrar mensaje IN
            var payload = msg.Attachments is { Count: > 0 } || (msg.Meta is { Count: > 0 })
                ? JsonSerializer.Serialize(new { msg.Attachments, msg.Meta })
                : null;

            await _sessions.AddIncomingMessageAsync(session.Id, msg.Text, payload, msg.ThreadId, ct);

            // 3) Log seguro (sin PII cruda)
            _logger.LogInformation("IN {channel}/{user}: {text}",
                msg.Channel, msg.ChannelUserId, PiiRedactor.Safe(msg.Text));

            // 4) Mini evento (opcional)
            await _sessions.AddEventAsync(
                    sessionId: session.Id,
                    type: "Ingest",
                    result: "ok",
                    dataJson: null,
                    ct: ct
            );

            return Ok(new
            {
                accepted = true,
                channel = msg.Channel,
                sessionId = session.Id,
                cxSessionPath = session.CxSessionPath
            });
        }
    }
}
