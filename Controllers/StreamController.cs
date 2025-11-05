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
            // 1) MINI-VALIDACIÓN JWT (igual que antes)
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

            // 2) HEADERS SSE (igual + ajustes para proxies / buffering)
            HttpContext.Response.Headers.Append("Content-Type", "text/event-stream");
            HttpContext.Response.Headers.Append("Cache-Control", "no-cache");
            HttpContext.Response.Headers.Append("X-Accel-Buffering", "no"); // Evita buffering en Nginx/proxies
            HttpContext.Response.Headers.Append("Connection", "keep-alive");

            // (nuevo) Desactiva buffering y envía preámbulo estándar SSE:
            HttpContext.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpResponseBodyFeature>()
                ?.DisableBuffering();

            // Sugerencia de reconexión para el EventSource del navegador (no dispara handlers)
            await Response.WriteAsync("retry: 15000\n:open\n\n");
            await Response.Body.FlushAsync();

            var sub = _redis.GetSubscriber();
            var chan = new RedisChannel($"{_channelPrefix}{sessionId}", RedisChannel.PatternMode.Literal);

            // Helper de eventos (igual que el tuyo)
            async Task Send(string evtName, string json)
            {
                var sb = new StringBuilder();
                sb.Append("event: ").Append(evtName).Append('\n');
                sb.Append("data: ").Append(json).Append('\n').Append('\n');
                await HttpContext.Response.WriteAsync(sb.ToString());
                await HttpContext.Response.Body.FlushAsync();
            }

            // 3) READY inicial (igual que antes)
            await Send("ready", "{\"ok\":true}");

            // 4) Snapshot del último turno (igual que antes)
            var db = _redis.GetDatabase();
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

            // 5) Suscripción a Redis Pub/Sub (igual que antes)
            await sub.SubscribeAsync(chan, async (c, msg) =>
            {
                try
                {
                    var json = (string)msg!;
                    using var doc = JsonDocument.Parse(json);
                    var type = doc.RootElement.GetProperty("type").GetString() ?? "message";
                    await Send(type, json);
                }
                catch
                {
                    // Ignora errores de parseo puntual para no cerrar el stream
                }
            });

            // 6) Mantener viva la conexión con heartbeats (nuevo)
            // En Cloud Run/proxies, si no sale ningún byte por un rato, cortan la conexión.
            // Los comentarios SSE (líneas que empiezan con ':') NO llegan al frontend.
            var heartbeatEvery = TimeSpan.FromSeconds(20);
            var hb = new PeriodicTimer(heartbeatEvery);

            try
            {
                while (await hb.WaitForNextTickAsync(HttpContext.RequestAborted))
                {
                    await Response.WriteAsync($":hb {DateTime.UtcNow:o}\n\n", HttpContext.RequestAborted);
                    await Response.Body.FlushAsync(HttpContext.RequestAborted);
                }
            }
            catch (OperationCanceledException)
            {
                // cliente cerró, salir en paz
            }
            finally
            {
                await sub.UnsubscribeAsync(chan);
            }

            return new EmptyResult();
        }
    }
}
