using BotApp.Helpers;
using Microsoft.AspNetCore.Mvc;
using StackExchange.Redis;
using System.Text;
using System.Text.Json;

namespace BotApp.Controllers
{
    [Route("stream")]
    [ApiController]
    public class StreamController : ControllerBase
    {
        private readonly IConnectionMultiplexer _redis;
        private readonly string _channelPrefix;
        private readonly IConfiguration _cfg;

        public StreamController(IConnectionMultiplexer redis, IConfiguration cfg)
        {
            _redis = redis;
            _cfg = cfg;
            _channelPrefix = cfg["SSE:ChannelPrefix"] ?? "sse:";
        }

        [HttpGet]
        public async Task<IActionResult> Get([FromQuery] Guid sessionId, [FromQuery(Name = "access_token")] string? jwt = null)
        {
            // MINI-VALIDACIÓN: valida el JWT 
            if (string.IsNullOrWhiteSpace(jwt)) return Unauthorized();

            try
            {
                var principal = JwtSecurityTokenHelper.Validate(jwt, _cfg, out _);
                var sidClaim = principal.FindFirst("sid")?.Value;
                if (!Guid.TryParse(sidClaim, out var sidFromToken) || sidFromToken != sessionId)
                    return Unauthorized();
            }
            catch
            {
                return Unauthorized();
            }
            // En dev podrías permitir vacío, en prod exigirlo.
            HttpContext.Response.Headers.Append("Content-Type", "text/event-stream");
            HttpContext.Response.Headers.Append("Cache-Control", "no-cache");
            HttpContext.Response.Headers.Append("X-Accel-Buffering", "no"); // Nginx
            HttpContext.Response.Headers.Append("Connection", "keep-alive");

            var sub = _redis.GetSubscriber();
            var chan = new RedisChannel($"{_channelPrefix}{sessionId}", RedisChannel.PatternMode.Literal);

            // Pequeño helper para escribir eventos SSE
            async Task Send(string evtName, string json)
            {
                var sb = new StringBuilder();
                sb.Append("event: ").Append(evtName).Append('\n');
                sb.Append("data: ").Append(json).Append('\n').Append('\n');
                await HttpContext.Response.WriteAsync(sb.ToString());
                await HttpContext.Response.Body.FlushAsync();
            }

            // Notifica que el stream está listo
            await Send("ready", "{\"ok\":true}");

            var db = _redis.GetDatabase();
            var pattern = $"sse:last:{sessionId}:*"; // si quieres el último de cualquier turnId

            // opción simple: si guardas también "el último turnId" por sesión
            var lastTurnKey = $"sse:last:{sessionId}:turn";
            var lastTurnId = await db.StringGetAsync(lastTurnKey);
            if (!lastTurnId.IsNullOrEmpty)
            {
                var snapKey = $"sse:last:{sessionId}:{lastTurnId}";
                var snap = await db.StringGetAsync(snapKey);
                var snapStr = (string?)snap;
                if (!string.IsNullOrEmpty(snapStr))
                {
                    using var doc = JsonDocument.Parse(snapStr);
                    var type = doc.RootElement.GetProperty("type").GetString() ?? "message";
                    await Send(type, snapStr);
                }
            }

            // Suscripción a Redis Pub/Sub
            await sub.SubscribeAsync(chan, async (c, msg) =>
            {
                try
                {
                    var json = (string)msg!;
                    // Podrías inspeccionar el "type" para usarlo como nombre de evento
                    using var doc = JsonDocument.Parse(json);
                    var type = doc.RootElement.GetProperty("type").GetString() ?? "message";
                    await Send(type, json);
                }
                catch
                {
                    // Ignora errores de parseo puntual para no cerrar el stream
                }
            });

            // Mantener la conexión abierta hasta que el cliente se desconecte
            // Aquí simplemente "duerme" y verifica si el cliente cerró.
            while (!HttpContext.RequestAborted.IsCancellationRequested)
                await Task.Delay(1000, HttpContext.RequestAborted);

            await sub.UnsubscribeAsync(chan);
            return new EmptyResult();
        }
    }
}
