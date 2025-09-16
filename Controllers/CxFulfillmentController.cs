using BotApp.Filters;
using BotApp.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using static BotApp.DTO.Fulfillment.Fulfillment;

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
        public CxFulfillmentController(
           IConfiguration cfg,
           DenunciasService denuncias,
           ExpedientesService expedientes,
           GeminiRagService rag,
           SessionStateStore state,
           IConversationRagService convRag)
        {
            _cfg = cfg;
            _denuncias = denuncias;
            _expedientes = expedientes;
            _rag = rag;
            _state = state;
            _convRag = convRag;
        }

        [HttpPost]
        [ServiceFilter(typeof(CxApiKeyFilter))]
        public async Task<IActionResult> Post([FromBody] FulfillmentRequest body, CancellationToken ct)
        {
            var expected = _cfg["Cx:WebhookApiKey"];
            if (!CxApiKeyAuth.IsValid(Request, expected))
                return Unauthorized();

            var tag = body.fulfillmentInfo?.tag ?? "";
            var userId = body.sessionInfo?.parameters?.GetValueOrDefault("channelUserId")?.ToString();

            string reply = tag switch
            {
                "CrearDenuncia" => await HandleCrearDenuncia(body, userId, ct),
                "ConsultarExpediente" => await HandleConsultarExpediente(body, ct),
                "QnA" => await HandleQnA(body, ct),
                _ => "Lo siento, no entendí la acción solicitada."
            };

            return Ok(new
            {
                fulfillmentResponse = new FulfillmentResponse
                {
                    messages = new[]
                    {
                        new FulfillmentMessage
                        {
                            text = new FulfillmentText { text = new[] { reply } }
                        }
                    }
                }
            });
        }

        private async Task<string> HandleCrearDenuncia(FulfillmentRequest body, string? userId, CancellationToken ct)
        {
            var p = body.sessionInfo.parameters!;
            var dto = new DTO.Denuncias.CreateDenunciaDto
            {
                SessionId = Guid.Parse(p["sessionId"].ToString()!),
                Nombre = p["nombre"].ToString()!,
                Cedula = p["cedula"].ToString()!,
                Ubicacion = p["ubicacion"].ToString()!,
                Descripcion = p["descripcion"].ToString()!
            };

            var resp = await _denuncias.CreateAsync(dto, ct);
            return $"Denuncia #{resp.Id} creada correctamente.";
        }

        private async Task<string> HandleConsultarExpediente(FulfillmentRequest body, CancellationToken ct)
        {
            var p = body.sessionInfo.parameters!;
            var numero = p["numeroExpediente"].ToString()!;
            var resp = await _expedientes.GetByNumeroAsync(numero, ct);
            return resp == null
                ? $"No encontré el expediente {numero}."
                : $"El expediente {resp.Numero} está en estado: {resp.Estado}.";
        }
        /// <summary>
        /// Manejador 
        /// </summary>
        /// <param name="body"></param>
        /// <param name="ct"></param>
        /// <returns></returns>
        private async Task<string> HandleQnA(FulfillmentRequest body, CancellationToken ct)
        {
            var pregunta = body.sessionInfo?.parameters?["q"]?.ToString()?.Trim();
            var sid = Guid.Parse(body.sessionInfo?.parameters?["sessionId"]?.ToString()?.Trim());

            if (sid == Guid.Empty) return "Error interno: sesión inválida.";
            if (string.IsNullOrWhiteSpace(pregunta))
                return "¿Podría indicarme su consulta con un poco más de detalle?";

            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(TimeSpan.FromSeconds(20));

                //  usa el orquestador que combina historia + Discovery + Gemini
                var answer = await _convRag.AskAsync(sid, pregunta, cts.Token);

                return string.IsNullOrWhiteSpace(answer)
                    ? "No encuentro información suficiente para darle una respuesta exacta en este momento."
                    : answer;
            }
            catch (OperationCanceledException)
            {
                return "Estoy tardando más de lo normal. Por favor, intente de nuevo en unos segundos.";
            }
            catch (Exception)
            {
                return "Tuvimos un inconveniente al consultar la información. Por favor, inténtelo de nuevo.";
            }
        }

    }
}
