using BotApp.Extensions;
using BotApp.Filters;
using BotApp.Services;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using static BotApp.DTO.Fulfillment.Fulfillment;

namespace BotApp.Controllers
{
    [Route("cx/fulfillment")]
    [ApiController]
    public class CxFulfillmentController : ControllerBase
    {
        private readonly IConfiguration _cfg;
        private readonly ISseEmitter _sse;
        private readonly ILogger<CxFulfillmentController> _logger;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly string startPage;


        public CxFulfillmentController(
           IConfiguration cfg,
           ISseEmitter sse,
           ILogger<CxFulfillmentController> logger,
           IServiceScopeFactory scopeFactory)      // ← opcional
        {
            _cfg = cfg;
            _sse = sse;
            _logger = logger;
            _scopeFactory = scopeFactory;
            startPage = GetPagePath(_cfg["Cx:StartPageId"] ?? "START_PAGE");
        }

        [HttpPost]
        [ServiceFilter(typeof(CxApiKeyFilter))]
        public async Task<IActionResult> Post([FromBody] FulfillmentRequest body, CancellationToken ct)
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
                    AcceptAndRun(sp => RunCrearDenunciaAsync(sp, p, userId, sessionId, turnId, CancellationToken.None));
                    break;

                case "ConsultarExpediente":
                    resets = new()
                    {
                        ["numeroexpediente"] = null
                    };
                    AcceptAndRun(sp => RunConsultarExpedienteAsync(sp, p, sessionId, turnId, CancellationToken.None));
                    break;

                case "QnA":
                    resets = new()
                    {
                        ["q"] = null
                    };
                    AcceptAndRun(sp => RunQnAAsync(sp, p, sessionId, turnId, CancellationToken.None));
                    break;
                case "EnviarCodigoExpediente":
                    var result = await EnvioCodigoAsyncScoped(p, sessionId, turnId, ct);
                    _logger.LogDebug("Resultado EnvioCodigoExpediente: {ResultJson}", JsonSerializer.Serialize(result));

                    return Ok(result);

                case "ValidarCodigoExpediente":
                    resets = new()
                    {
                        ["numeroExpediente"] = null,
                        ["codigoVerificacion"] = null
                    };
                    AcceptAndRun(sp => ValidacionCodigoAsync(sp, p, sessionId, turnId, CancellationToken.None));
                    break;

                case "ValidateParam":
                    return Ok(ValidateParam(body));


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
                    _logger.LogError(ex, "Error en AcceptAndRun");
                }
            });
        }

        private async Task RunCrearDenunciaAsync(IServiceProvider sp, IDictionary<string, object> p,
    string? userId, Guid sessionId, string turnId, CancellationToken ct)
        {
            try
            {
                var denuncias = sp.GetRequiredService<DenunciasService>();   // ← RESUELTO EN SCOPE NUEVO

                if (string.IsNullOrWhiteSpace(userId))
                {
                    await _sse.EmitError(sessionId.ToString(), turnId, "MISSING_USER_ID", "Falta el ID de usuario.", true);
                    await _sse.EmitDone(sessionId.ToString(), turnId);
                    return;
                }

                var dto = new DTO.Denuncias.CreateDenunciaDto
                {
                    SessionId = sessionId,
                    Nombre = p["nombre"]?.ToString()!,
                    Cedula = p["cedula"]?.ToString()!,
                    Ubicacion = p["ubicacion"]?.ToString()!,
                    Descripcion = p["descripcion"]?.ToString()!
                };

                await _sse.EmitTool(sessionId.ToString(), turnId, "db", "start");
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                var resp = await denuncias.CreateAsync(dto, cts.Token);
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
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                var resp = await expedientes.GetByNumeroAsync(numero, cts.Token);

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

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(45));
                
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
            catch (Grpc.Core.RpcException rex) when (rex.StatusCode == Grpc.Core.StatusCode.Cancelled)
            {
                await _sse.EmitError(sessionId.ToString(), turnId, "RAG_TIMEOUT",
                    "Estoy tardando más de lo normal. Por favor, intente de nuevo en unos segundos.", true);
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

                if (string.IsNullOrWhiteSpace(numero) || string.IsNullOrWhiteSpace(codigo))
                {
                    await _sse.EmitError(sessionId.ToString(), turnId, "MISSING_PARAM",
                        "Faltan parámetros requeridos (número de expediente o código).", true);
                    await _sse.EmitDone(sessionId.ToString(), turnId);
                    return;
                }
                var codigoSvc = sp.GetRequiredService<CodigoVerificacionService>();
                var expedientes = sp.GetRequiredService<ExpedientesService>();


                _logger.LogDebug($"Validando código {codigo} para expediente {numero}");
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

                var esValido = await codigoSvc.ValidarAsync(numero!, codigo!);
                if (!esValido)
                {
                    await _sse.EmitFinal(sessionId.ToString(), turnId,
                        $"❌ El código ingresado es inválido o ha expirado. Por favor, iniciá de nuevo la consulta.",
                        new { codigoValido = false });
                    await _sse.EmitDone(sessionId.ToString(), turnId);
                    return;
                }

                _logger.LogDebug("Código válido");

                // Código correcto → traer expediente
                var expediente = await expedientes.GetByNumeroAsync(numero!, cts.Token);
                var estado = expediente?.Estado ?? "Desconocido";

                await _sse.EmitFinal(sessionId.ToString(), turnId,
                    $"✅ Código correcto. El expediente {numero} está en estado: {estado}.",
                    new { numero, estado, codigoValido = true });

                await _sse.EmitDone(sessionId.ToString(), turnId);
            }
            catch (Exception ex)
            {
                await _sse.EmitError(sessionId.ToString(), turnId, "VALIDACION_ERROR", ex.Message, false);
                await _sse.EmitDone(sessionId.ToString(), turnId);
            }
        }
        private async Task<object> EnvioCodigoAsyncScoped(
            IDictionary<string, object> p,
            Guid sessionId,
            string turnId,
            CancellationToken ct)
        {
            using var scope = _scopeFactory.CreateScope();
            return await EnvioCodigoAsync(scope.ServiceProvider, p, sessionId, turnId, ct);
        }
        private async Task<object> EnvioCodigoAsync(
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
                if (string.IsNullOrWhiteSpace(numero))
                {
                    await _sse.EmitFinal(sessionId.ToString(), turnId,
                       "❌ Faltan datos requeridos. Iniciá nuevamente la consulta.",
                       new { codigoValido = false });
                    await _sse.EmitDone(sessionId.ToString(), turnId);

                    return new
                    {
                        targetPage = startPage,
                        sessionInfo = new
                        {
                            parameters = new { codigoValido = false }
                        },
                        fulfillmentResponse = new
                        {
                            messages = Array.Empty<object>()
                        }
                    };
                }
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(20));
                var expedientes = sp.GetRequiredService<ExpedientesService>();
                var emailService = sp.GetRequiredService<IEmailService>();
                var codigoSvc = sp.GetRequiredService<CodigoVerificacionService>();
                _logger.LogDebug($"Iniciando envío de código para expediente {numero}");

                // 1️⃣ Validar que el expediente exista
                var expediente = await expedientes.GetByNumeroAsync(numero!, cts.Token);
                if (expediente == null)
                {
                    _logger.LogDebug($"No se encontró el expediente {numero}");
                    await _sse.EmitFinal(sessionId.ToString(), turnId,
                        $"❌ No se encontró el expediente {numero}. Iniciá nuevamente la consulta.",
                        new { codigoValido = false });
                    await _sse.EmitDone(sessionId.ToString(), turnId);

                    return new
                    {
                        targetPage = startPage,
                        sessionInfo = new
                        {
                            parameters = new { codigoValido = false }
                        },
                        fulfillmentResponse = new
                        {
                            messages = Array.Empty<object>()
                        }
                    };
                }

                if (string.IsNullOrWhiteSpace(expediente.Email))
                {
                    await _sse.EmitFinal(sessionId.ToString(), turnId,
                        $"❌ El expediente {numero} no tiene correo electrónico registrado.",
                        new { codigoValido = false });
                    await _sse.EmitDone(sessionId.ToString(), turnId);

                    return new
                    {
                        targetPage = startPage,
                        sessionInfo = new
                        {
                            parameters = new { codigoValido = false }
                        },
                        fulfillmentResponse = new
                        {
                            messages = Array.Empty<object>()
                        }
                    };
                }
                // Generar código y enviarlo por email
                _logger.LogDebug($"Enviando código para expediente {numero}");
                _logger.LogDebug($"Correo registrado: {expediente.Email}");

                // Generar código y guardarlo con TTL
                var codigo = await codigoSvc.GenerarAsync(numero!);

                _logger.LogDebug($"Código generado: {codigo}");

                // 3️⃣ Armar el correo
                var subject = $"Código de verificación para expediente {numero}";
                var body = $@"
                        <p>Estimado/a usuario/a,</p>
                        <p>Su código de verificación para el expediente <b>{numero}</b> es:</p>
                        <h2 style='color:#0078D4;'>{codigo}</h2>
                        <p>Este código expirará en 10 minutos.</p>
                        <p>Atentamente,<br>Defensoría de los Habitantes</p>";

                // 4️⃣ Enviar el correo
                await _sse.EmitProgress(sessionId.ToString(), turnId, $"📩 Enviando código…");
                await emailService.SendEmailAsync(expediente.Email, subject, body, true);


                await _sse.EmitFinal(sessionId.ToString(), turnId,
                    $"✉️ Código enviado al correo registrado para el expediente {numero}. Ingresalo para continuar.",
                    new { numero });

                await _sse.EmitDone(sessionId.ToString(), turnId);
                return new
                {
                    fulfillmentResponse = new { messages = Array.Empty<object>() },
                    sessionInfo = new { parameters = new { codigoValido = true, numeroExpediente = numero } }
                };
            }
            catch (Exception ex)
            {
                return new
                {
                    targetPage = startPage,
                    sessionInfo = new
                    {
                        parameters = new { codigoValido = false }
                    },
                    fulfillmentResponse = new
                    {
                        messages = Array.Empty<object>()
                    }
                };
            }
        }

        private object ValidateParam(FulfillmentRequest body)
        {
            _logger.LogDebug("Ejecutando ValidateParam...");

            var cancelWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                "cancelar","cancela","cancelá","cancele",
                "detén","detener","parar","stop",
                "olvídalo","olvidalo","mejor no","no seguir","no quiero seguir"
            };

            var pinfo = body?.pageInfo?.formInfo?.parameterInfo ?? new List<ParameterInfo>();
            if (pinfo.Count == 0)
                return new { };

            var active =pinfo.Find(p => p.justCollected == true) ??
                pinfo.Find(p => string.Equals(p.state, "EMPTY", StringComparison.OrdinalIgnoreCase));

            if (active == null || string.IsNullOrWhiteSpace(active.displayName))
                return new { };

            string? candidate = null;
            if (body.sessionInfo?.parameters != null &&
                body.sessionInfo.parameters.TryGetValue(active.displayName, out var raw))
                candidate = raw?.ToString()?.Trim();

            if (string.IsNullOrWhiteSpace(candidate) && active.value != null)
                candidate = active.value.ToString().Trim();

            if (string.IsNullOrWhiteSpace(candidate) && !string.IsNullOrWhiteSpace(body.text))
                candidate = body.text.Trim();

            var sessionId = body.sessionInfo?.parameters?.GetValueOrDefault("sessionId")?.ToString() ?? Guid.NewGuid().ToString();
            var turnId = Guid.NewGuid().ToString();

            // 🚨 Detección de cancelación
            if (!string.IsNullOrEmpty(candidate) && cancelWords.Contains(candidate))
            {
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await _sse.EmitFinal(sessionId, turnId,
                            "🚫 Se canceló la creación de la denuncia.",
                            new { cancelled = true, parameter = active.displayName });
                        await _sse.EmitDone(sessionId, turnId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error enviando SSE de cancelación");
                    }
                });

                var clear = new Dictionary<string, object>
                {
                    ["nombreDenunciante"] = null,
                    ["cedula"] = null,
                    ["ubicacion"] = null,
                    ["descripcion"] = null,
                    ["cancel"] = true
                };

                return new
                {
                    sessionInfo = new { parameters = clear },
                    fulfillmentResponse = new
                    {
                        mergeBehavior = "REPLACE",
                        messages = new object[]
                        {
                    new { text = new { text = new[] { "Cancelé la creación de tu denuncia. Si querés, podemos empezar de nuevo o hacer otra consulta." } } }
                        }
                    },
                    targetPage = startPage
                };
            }

            return new { };
        }
        private string GetPagePath(string pageId)
        {
            var agentPath = _cfg["Cx:AgentPath"];
            var flowId = _cfg["Cx:DefaultFlowId"];
            return $"{agentPath}/flows/{flowId}/pages/{pageId}";
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
