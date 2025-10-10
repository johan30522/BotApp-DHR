using BotApp.Filters;
using BotApp.Services;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using static BotApp.DTO.Fulfillment.Fulfillment;
using BotApp.Extensions;
using Microsoft.Extensions.DependencyInjection;

namespace BotApp.Controllers
{
    [Route("cx/fulfillment")]
    [ApiController]
    public class CxFulfillmentController : ControllerBase
    {
        private readonly IConfiguration _cfg;
        private readonly DenunciasService _denuncias;
        private readonly ExpedientesService _expedientes;
        private readonly GeminiRagService _rag;
        private readonly SessionStateStore _state;
        private readonly IConversationRagService _convRag;
        private readonly ISseEmitter _sse;             // ← NUEVO
        private readonly ILogger<CxFulfillmentController> _logger; // opcional, pero útil
        private readonly IServiceScopeFactory _scopeFactory;

        public CxFulfillmentController(
           IConfiguration cfg,
           DenunciasService denuncias,
           ExpedientesService expedientes,
           GeminiRagService rag,
           SessionStateStore state,
           IConversationRagService convRag,
           ISseEmitter sse,                              // ← NUEVO
           ILogger<CxFulfillmentController> logger,
           IServiceScopeFactory scopeFactory)      // ← opcional
        {
            _cfg = cfg;
            _denuncias = denuncias;
            _expedientes = expedientes;
            _rag = rag;
            _state = state;
            _convRag = convRag;
            _sse = sse;
            _logger = logger;
            _scopeFactory = scopeFactory;
        }

        [HttpPost]
        [ServiceFilter(typeof(CxApiKeyFilter))]
        public IActionResult Post([FromBody] FulfillmentRequest body, CancellationToken ct)
        {
            _logger.LogDebug("######Inicia webhook segun accion #####");
            var expected = _cfg["Cx:WebhookApiKey"];
            _logger.LogDebug("valida si esta autenticado");
            if (!CxApiKeyAuth.IsValid(Request, expected))
                return Unauthorized();

            var tag = body.fulfillmentInfo?.tag ?? "";
            var p = ParamsInsensitive(body);
            _logger.LogDebug("captura la sesion ");
            // Recibimos sessionId y turnId que mandaste desde /ingest
            var sessionId = p.ContainsKey("sessionId") && Guid.TryParse(p["sessionId"]?.ToString(), out var sid)
                ? sid
                : Guid.Empty;
            _logger.LogDebug("Obtiene el turno");
            _logger.LogDebug($"SessionId recibido: {sessionId}");
            var turnId = p.ContainsKey("turnId") ? (p["turnId"]?.ToString() ?? Guid.NewGuid().ToString()) : Guid.NewGuid().ToString();
            _logger.LogDebug($"TurnId recibido: {turnId}");

            var userId = body.sessionInfo?.parameters?.GetValueOrDefault("channelUserId")?.ToString();

            // Definimos resets por tag (igual que antes)
            Dictionary<string, object?>? resets = null;
            _logger.LogDebug("Prepara el trabajo en segundo plano");
            _logger.LogDebug($"Tag recibido: {tag}");


            // Encolamos el trabajo en 2° plano (accept-and-run)
            switch (tag)
            {
                case "CrearDenuncia":
                    resets = new()
                    {
                        ["nombre"] = null,
                        ["cedula"] = null,
                        ["ubicacion"] = null,
                        ["descripcion"] = null
                    };
                    AcceptAndRun(sp => RunCrearDenunciaAsync(sp, p, userId, sessionId, turnId, ct));
                    break;

                case "ConsultarExpediente":
                    resets = new()
                    {
                        ["numeroexpediente"] = null
                    };
                    AcceptAndRun(sp => RunConsultarExpedienteAsync(sp, p, sessionId, turnId, ct));
                    break;

                case "QnA":
                    resets = new()
                    {
                        ["q"] = null
                    };
                    AcceptAndRun(sp => RunQnAAsync(sp, p, sessionId, turnId, ct));
                    break;
                case "EnviarCodigoExpediente":
                    AcceptAndRun(sp => EnvioCodigoAsync(sp, p, sessionId, turnId, ct));
                    break;

                case "ValidarCodigoExpediente":
                    resets = new()
                    {
                        ["numeroExpediente"] = null,
                        ["codigoVerificacion"] = null
                    };
                    AcceptAndRun(sp => ValidacionCodigoAsync(sp, p, sessionId, turnId, ct));
                    break;

                default:
                    // Si llega algo no esperado, notificamos error por SSE y seguimos.
                    AcceptAndRun(async _ =>
                    {
                        await _sse.EmitError(sessionId.ToString(), turnId, "UNKNOWN_TAG",
                            $"Acción no reconocida: {tag}", retryable: false);
                        await _sse.EmitDone(sessionId.ToString(), turnId);
                    });
                    break;
            }

            // Respondemos RÁPIDO a CX (sin mensajes: el ACK ya salió en el fulfillment pre-webhook)
            // Nota: Puedes incluir payload con { accepted, turnId } si lo deseas.
            return Ok(new
            {
                fulfillmentResponse = new FulfillmentResponse
                {
                    messages = Array.Empty<FulfillmentMessage>() // ← sin mensajes aquí (evita duplicar con SSE)
                },
                sessionInfo = new
                {
                    parameters = resets
                },
                payload = new
                {
                    accepted = true,
                    turnId
                }
            });
        }

        // ---------- Jobs en 2° plano ----------

        private void AcceptAndRun(Func<IServiceProvider, Task> job)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    await job(scope.ServiceProvider);
                }
                catch (OperationCanceledException) { /* ignore */ }
                catch (Exception ex)
                {
                    try { _logger.LogError(ex, "Error en AcceptAndRun"); } catch { }
                }
            });
        }

        private async Task RunCrearDenunciaAsync(IServiceProvider sp, IDictionary<string, object> p,
    string? userId, Guid sessionId, string turnId, CancellationToken ct)
        {
            try
            {
                var denuncias = sp.GetRequiredService<DenunciasService>();   // ← RESUELTO EN SCOPE NUEVO

                if (string.IsNullOrWhiteSpace(userId)) userId = "user-123";

                var dto = new DTO.Denuncias.CreateDenunciaDto
                {
                    SessionId = sessionId,
                    Nombre = p["nombre"]?.ToString()!,
                    Cedula = p["cedula"]?.ToString()!,
                    Ubicacion = p["ubicacion"]?.ToString()!,
                    Descripcion = p["descripcion"]?.ToString()!
                };

                await _sse.EmitTool(sessionId.ToString(), turnId, "db", "start");
                var resp = await denuncias.CreateAsync(dto, ct);
                await _sse.EmitTool(sessionId.ToString(), turnId, "db", "end");

                await _sse.EmitFinal(sessionId.ToString(), turnId, $"Denuncia #{resp.Id} creada correctamente.",
                    new { id = resp.Id, source = "db" });
                await _sse.EmitDone(sessionId.ToString(), turnId);
            }
            catch (Exception ex)
            {
                await _sse.EmitError(sessionId.ToString(), turnId, "CREATE_DENUNCIA_ERROR", ex.Message, retryable: false);
                await _sse.EmitDone(sessionId.ToString(), turnId);
            }
        }

        private async Task RunConsultarExpedienteAsync(IServiceProvider sp, IDictionary<string, object> p,
    Guid sessionId, string turnId, CancellationToken ct)
        {
            try
            {
                var expedientes = sp.GetRequiredService<ExpedientesService>(); // ← RESUELTO EN SCOPE NUEVO
                var numero = (p.ContainsKey("numeroExpediente") ? p["numeroExpediente"] : p.GetValueOrDefault("numeroexpediente"))?.ToString();

                if (string.IsNullOrWhiteSpace(numero))
                { await _sse.EmitError(sessionId.ToString(), turnId, "MISSING_PARAM", "Falta el número de expediente.", true); await _sse.EmitDone(sessionId.ToString(), turnId); return; }

                await _sse.EmitProgress(sessionId.ToString(), turnId, $"Consultando expediente {numero}…");
                var resp = await expedientes.GetByNumeroAsync(numero, ct);

                var text = resp == null ? $"No encontré el expediente {numero}." : $"El expediente {resp.Numero} está en estado: {resp.Estado}.";
                await _sse.EmitFinal(sessionId.ToString(), turnId, text, new { numero, source = "db" });
                await _sse.EmitDone(sessionId.ToString(), turnId);
            }
            catch (Exception ex)
            {
                await _sse.EmitError(sessionId.ToString(), turnId, "EXPEDIENTE_ERROR", ex.Message, retryable: false);
                await _sse.EmitDone(sessionId.ToString(), turnId);
            }
        }

        private async Task RunQnAAsync(IServiceProvider sp, IDictionary<string, object> p,
                Guid sessionId, string turnId, CancellationToken ct)
        {
            try
            {
                _logger.LogDebug("inicia RunQnAAsync");
                var convRag = sp.GetRequiredService<IConversationRagService>(); // ← RESUELTO EN SCOPE NUEVO
                var pregunta = p.GetValueOrDefault("q")?.ToString()?.Trim();
                _logger.LogDebug($"La pregunta es : {pregunta}");
                if (string.IsNullOrWhiteSpace(pregunta))
                { await _sse.EmitError(sessionId.ToString(), turnId, "EMPTY_QUERY", "¿Podría indicarme su consulta con un poco más de detalle?", true); await _sse.EmitDone(sessionId.ToString(), turnId); return; }
                _logger.LogDebug("debug 1");
                await _sse.EmitTool(sessionId.ToString(), turnId, "discovery_search", "start");
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(30));
                await _sse.EmitProgress(sessionId.ToString(), turnId, "😴🕒 Estoy Redactando tu respuesta…", new { source = "gemini" });

                var answer = await convRag.AskAsync(sessionId, pregunta, cts.Token);
                await _sse.EmitTool(sessionId.ToString(), turnId, "discovery_search", "end");

                var final = string.IsNullOrWhiteSpace(answer) ? "No encuentro información suficiente para darle una respuesta exacta en este momento." : answer;
                await _sse.EmitFinal(sessionId.ToString(), turnId, final, new { source = "rag" });
                await _sse.EmitDone(sessionId.ToString(), turnId);
            }
            catch (OperationCanceledException)
            {
                await _sse.EmitError(sessionId.ToString(), turnId, "RAG_TIMEOUT", "Estoy tardando más de lo normal. Por favor, intente de nuevo en unos segundos.", true);
                await _sse.EmitDone(sessionId.ToString(), turnId);
            }
            catch (Exception ex)
            {
                await _sse.EmitError(sessionId.ToString(), turnId, "RAG_ERROR", "Tuvimos un inconveniente al consultar la información. Por favor, inténtelo de nuevo.", false);
                _logger.LogError(ex, "Error en QnA");
                await _sse.EmitDone(sessionId.ToString(), turnId);
            }
        }
        private async Task ValidacionCodigoAsync(
            IServiceProvider sp,
            IDictionary<string, object> p,
            Guid sessionId,
            string turnId,
            CancellationToken ct
        )
        {
            try
            {
                var numero = p.GetValueOrDefault("numeroExpediente")?.ToString();
                var codigo = p.GetValueOrDefault("codigoVerificacion")?.ToString();

                _logger.LogDebug($"Validando código {codigo} para expediente {numero}");


                await _sse.EmitProgress(sessionId.ToString(), turnId, $"🕐 Validando Código…");

                await Task.Delay(500);

                if (codigo == "123456")
                {
                    await _sse.EmitFinal(sessionId.ToString(), turnId,
                        $"✅ Código correcto. El expediente {numero} está en estado: FINAL.",
                        new { numero, estado = "FINAL", codigoValido = true });
                }
                else
                {
                    await _sse.EmitFinal(sessionId.ToString(), turnId,
                        $"❌ El código ingresado es inválido o ha expirado. Por favor, iniciá de nuevo la consulta.",
                        new { codigoValido = false });
                }

                await _sse.EmitDone(sessionId.ToString(), turnId);
            }
            catch (Exception ex)
            {
                await _sse.EmitError(sessionId.ToString(), turnId, "VALIDACION_ERROR", ex.Message, false);
                await _sse.EmitDone(sessionId.ToString(), turnId);
            }
        }
        private async Task EnvioCodigoAsync(
            IServiceProvider sp,
            IDictionary<string, object> p,
            Guid sessionId,
            string turnId,
            CancellationToken ct
        )
        {
            try
            {
                var numero = p.GetValueOrDefault("numeroExpediente")?.ToString();

                _logger.LogDebug($"Enviando código para expediente {numero}");

                await _sse.EmitProgress(sessionId.ToString(), turnId, $"📩 Enviando código…");

                // Espera ficticia
                await Task.Delay(500);

                await _sse.EmitFinal(sessionId.ToString(), turnId,
                    $"✉️ Código enviado al correo registrado para el expediente {numero}. Ingresalo para continuar.",
                    new { numero });

                await _sse.EmitDone(sessionId.ToString(), turnId);
            }
            catch (Exception ex)
            {
                await _sse.EmitError(sessionId.ToString(), turnId, "ENVIO_ERROR", ex.Message, false);
                await _sse.EmitDone(sessionId.ToString(), turnId);
            }
        }

        private static IDictionary<string, object> ParamsInsensitive(FulfillmentRequest body)
        {
            return new Dictionary<string, object>(
                body.sessionInfo?.parameters ?? new(),
                StringComparer.OrdinalIgnoreCase
            );
        }
    }
}
