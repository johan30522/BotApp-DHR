using BotApp.Infrastructure.Logging;
using BotApp.Models;
using BotApp.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace BotApp.Controllers
{
    [Route("ingest")]
    [ApiController]
    public class IngestController : ControllerBase
    {
        private readonly SessionService _sessions;
        private readonly ILogger<IngestController> _logger;

        public IngestController(SessionService sessions, ILogger<IngestController> logger)
        {
            _sessions = sessions;
            _logger = logger;
        }



        [HttpPost("web")]
        public async Task<IActionResult> Web([FromBody] UniMessage msg, CancellationToken ct)
        {
            return await HandleIngest(msg with { Channel = "web" }, ct);
        }

        [HttpPost("whatsapp")]
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
