using BotApp.Filters;
using BotApp.Models;
using BotApp.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace BotApp.Controllers
{
    [Route("ingest/twilio")]
    [ApiController]
    public class TwilioWebhookController : ControllerBase
    {
        private readonly SessionService _sessions;
        public TwilioWebhookController(SessionService sessions) => _sessions = sessions;

        [HttpPost]
        [ServiceFilter(typeof(TwilioSignatureFilter))] // si estás usando el filtro de firma
        public async Task<IActionResult> Twilio([FromForm] Dictionary<string, string> form, CancellationToken ct)
        {
            // Campos típicos de Twilio
            var from = form.GetValueOrDefault("From") ?? "unknown";
            var body = form.GetValueOrDefault("Body");
            var messageSid = form.GetValueOrDefault("MessageSid") ?? Guid.NewGuid().ToString("N");

            // Usamos el MessageSid como IdempotencyKey (perfecto para evitar duplicados)
            var idempotencyKey = $"twilio:{messageSid}";

            // ThreadId opcional (podés usar el propio MessageSid)
            var threadId = form.GetValueOrDefault("SmsSid") ?? messageSid;

            // Meta debe ser Dictionary<string,string> (según tu clase)
            // Aplanamos el form a "k=v;k2=v2" para no romper el tipo
            var meta = new Dictionary<string, string>
            {
                ["twilio"] = string.Join(";", form.Select(kv => $"{kv.Key}={kv.Value}"))
            };

            // ✅ CONSTRUCTOR POSICIONAL (sin nombres)
            var msg = new UniMessage(
                idempotencyKey,    // 0
                "whatsapp",        // 1
                from,              // 2
                threadId,          // 3
                body,              // 4
                null,              // 5 attachments
                meta               // 6
            );

            // Persistencia que ya tenés
            var session = await _sessions.GetOrCreateAsync("whatsapp", from, null, ct);
            await _sessions.AddIncomingMessageAsync(session.Id, msg.Text, null, messageSid, ct);
            await _sessions.AddEventAsync(session.Id, type: "Ingest", result: "ok", dataJson: null, ct: ct);

            return Ok();
        }

    }

}
