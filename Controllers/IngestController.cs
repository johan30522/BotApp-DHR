using BotApp.Filters;
using BotApp.Infrastructure.Logging;
using BotApp.Models;
using BotApp.Services;
using Google.Cloud.Dialogflow.Cx.V3;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
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
        private readonly CxDetectService _cx;
        private readonly ISseEmitter _sse;


        public IngestController(SessionService sessions, TokenService tokens, ILogger<IngestController> logger, CxDetectService cx, ISseEmitter sse)
        {
            _tokens = tokens;
            _sessions = sessions;
            _logger = logger;
            _cx = cx;
            _sse = sse;
        }



        [HttpPost("web")]
        [AllowAnonymous]
        public async Task<IActionResult> Web([FromBody] UniMessage msg, CancellationToken ct)
        {
            var reqId = HttpContext.TraceIdentifier ?? Guid.NewGuid().ToString("N");
            var scopeState = new Dictionary<string, object?>
            {
                ["reqId"] = reqId,
                ["channel"] = "web",
                ["channelUserId"] = msg?.ChannelUserId
            };

            using (_logger.BeginScope(scopeState))
            {
                _logger.LogDebug("↪️ Ingreso a Web() con payload: text='{text}', attachments={attCount}, meta={metaCount}",
                    PiiRedactor.Safe(msg?.Text), msg?.Attachments?.Count ?? 0, msg?.Meta?.Count ?? 0);

                try
                {
                    // ¿Viene Authorization? Intentamos autenticar con el esquema JWT
                    var auth = await HttpContext.AuthenticateAsync(JwtBearerDefaults.AuthenticationScheme);
                    _logger.LogDebug("######Verifica si esta autenticado#####");
                    _logger.LogDebug("Auth.Succeeded={Succeeded}, IsAuthenticated={IsAuth}",
                        auth.Succeeded, auth.Principal?.Identity?.IsAuthenticated);

                    if (auth.Succeeded && auth.Principal?.Identity?.IsAuthenticated == true)
                    {
                        // ----- MODO MENSAJE (autenticado) -----
                        var user = auth.Principal;
                        var claimChannel = user.FindFirst("channel")?.Value;
                        var claimSub = user.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? user.FindFirst("sub")?.Value;
                        var claimSid = user.FindFirst("sid")?.Value;

                        _logger.LogDebug("Claims: channel='{channel}', sub='{sub}', sid='{sid}'",
                            claimChannel, claimSub, claimSid);

                        if (!string.Equals(claimChannel, "web", StringComparison.OrdinalIgnoreCase))
                        {
                            _logger.LogWarning("❌ Token inválido: canal incorrecto. channel='{channel}'", claimChannel);
                            return Unauthorized("Token inválido: canal incorrecto.");
                        }
                        if (!string.Equals(claimSub, msg.ChannelUserId, StringComparison.Ordinal))
                        {
                            _logger.LogWarning("❌ Token inválido: usuario no corresponde. sub='{sub}', body.ChannelUserId='{cid}'",
                                claimSub, msg.ChannelUserId);
                            return Unauthorized("Token inválido: usuario no corresponde.");
                        }

                        // Usamos sessionId del token si existe; si no, GetOrCreate por userId
                        Guid sessionId = Guid.TryParse(claimSid, out var sid) ? sid : Guid.Empty;
                        BotApp.Models.Session session;

                        if (sessionId != Guid.Empty)
                        {
                            _logger.LogDebug("Se recibió SID en token: {sid}", sessionId);
                            session = await _sessions.GetOrCreateAsync("web", msg.ChannelUserId, null, ct);
                            if (session.Id != sessionId)
                            {
                                _logger.LogWarning("SID del token ({sidToken}) no coincide con SID persistido ({sidPersistido}). Usando persistido.",
                                    sessionId, session.Id);
                                sessionId = session.Id;
                            }
                        }
                        else
                        {
                            _logger.LogDebug("No hay SID en token. Creando/obteniendo sesión por ChannelUserId...");
                            session = await _sessions.GetOrCreateAsync("web", msg.ChannelUserId, null, ct);
                            sessionId = session.Id;
                        }

                        // Enriquecemos el scope con el SID real
                        scopeState["sessionId"] = sessionId;
                        _logger.LogInformation("✅ Sesión resuelta: {sessionId}", sessionId);

                        var payload = msg.Attachments is { Count: > 0 } || (msg.Meta is { Count: > 0 })
                            ? JsonSerializer.Serialize(new { msg.Attachments, msg.Meta })
                            : null;

                        await _sessions.AddIncomingMessageAsync(sessionId, msg.Text, payload, msg.ThreadId, ct);

                        _logger.LogInformation("IN web/{user}: {text}", msg.ChannelUserId, PiiRedactor.Safe(msg.Text));
                        await _sessions.AddEventAsync(sessionId, type: "Ingest", result: "ok", dataJson: null, ct: ct);

                        var turnId = Guid.NewGuid().ToString();
                        // ----- Llamada a CX Detect -----
                        var cxParams = new Dictionary<string, object?>
                        {
                            ["sessionId"] = sessionId.ToString(),
                            ["channelUserId"] = msg.ChannelUserId,
                            ["turnId"] = turnId
                        };

                        _logger.LogDebug("→ CX Detect: session='{session}', text='{text}', params={params}",
                            sessionId, PiiRedactor.Safe(msg.Text ?? ""), JsonSerializer.Serialize(cxParams));

                        var sw = System.Diagnostics.Stopwatch.StartNew();
                        DetectIntentResponse cxResp;
                        try
                        {
                            cxResp = await _cx.DetectAsync(
                                sessionId.ToString(),
                                msg.Text ?? "",
                                cxParams,
                                ct
                            );
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "🔥 Error al invocar CX.DetectAsync");
                            await _sessions.AddEventAsync(sessionId, type: "CxDetect", result: "error",
                                dataJson: JsonSerializer.Serialize(new { ex.Message, ex.StackTrace }), ct: ct);
                            return StatusCode(StatusCodes.Status502BadGateway, "Error consultando el bot (CX).");
                        }
                        finally
                        {
                            sw.Stop();
                            _logger.LogDebug("CX Detect tomó {ms} ms", sw.ElapsedMilliseconds);
                        }

                        // 3) Por cada mensaje de CX (pre-webhook), emitir ACK por SSE
                        var respMsgs = cxResp.QueryResult?.ResponseMessages ?? new Google.Protobuf.Collections.RepeatedField<Google.Cloud.Dialogflow.Cx.V3.ResponseMessage>();


                        foreach (var r in respMsgs)
                        {
                            var texts = r.Text?.Text_;
                            if (texts is { Count: > 0 })
                            {
                                var text = string.Join("\n", texts);
                                _logger.LogDebug("→ SSE ACK: {text}", PiiRedactor.Safe(text));
                                await _sse.EmitAck(sessionId.ToString(), turnId, text, new { source = "cx" });
                            }
                        }

                        await _sessions.AddEventAsync(sessionId, type: "CxDetect", result: "ok", dataJson: null, ct: ct);

                        return Ok(new
                        {
                            accepted = true,
                            mode = "message",
                            channel = "web",
                            sessionId,
                            turnId,
                            cxSessionPath = session.CxSessionPath
                        });
                    }
                    else
                    {
                        _logger.LogDebug("######No esta autenticado#####");

                        // ----- MODO BOOTSTRAP (anónimo) -----
                        _logger.LogDebug("Modo Bootstrap anónimo. ChannelUserId='{cid}'", msg.ChannelUserId);

                        if (string.IsNullOrWhiteSpace(msg.ChannelUserId))
                        {
                            _logger.LogWarning("❌ Bootstrap rechazado: ChannelUserId vacío");
                            return BadRequest("ChannelUserId requerido para bootstrap.");
                        }

                        var session = await _sessions.GetOrCreateAsync("web", msg.ChannelUserId, null, ct);
                        scopeState["sessionId"] = session.Id;

                        var token = _tokens.IssueWebToken(session.Id, msg.ChannelUserId);
                        await _sessions.AddEventAsync(session.Id, type: "Bootstrap", result: "ok", dataJson: null, ct: ct);

                        _logger.LogInformation("✅ Bootstrap emitido: sessionId={sid}", session.Id);

                        return Ok(new
                        {
                            accepted = true,
                            mode = "bootstrap",
                            channel = "web",
                            sessionId = session.Id,
                            token,
                            warmup = "¡Hola! Gracias por comunicarte con la Defensoría de los Habitantes.\nEstoy aquí para acompañarle en lo que necesite:\n\n📂 Consultar su expediente\n📝 Presentar una denuncia\nℹ️ Obtener información sobre nuestros servicios\n\n¿Qué desea hacer hoy?"
                        });
                    }
                }
                catch (OperationCanceledException)
                {
                    _logger.LogWarning("⚠️ Petición cancelada (OperationCanceledException).");
                    return StatusCode(StatusCodes.Status499ClientClosedRequest);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "💥 Excepción no controlada en Web()");
                    return StatusCode(StatusCodes.Status500InternalServerError, "Error interno procesando la solicitud.");
                }
                finally
                {
                    _logger.LogDebug("↩️ Saliendo de Web()");
                }
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
