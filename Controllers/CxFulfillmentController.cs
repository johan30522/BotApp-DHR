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

            string reply;
            Dictionary<string, object?>? resets = null;

            switch (tag)
            {
                case "CrearDenuncia":
                    reply = await HandleCrearDenuncia(body, userId, ct);
                    resets = new Dictionary<string, object?>
                    {
                        ["nombre"] = null,
                        ["cedula"] = null,
                        ["ubicacion"] = null,
                        ["descripcion"] = null
                    };
                    break;

                case "ConsultarExpediente":
                    reply = await HandleConsultarExpediente(body, ct);
                    resets = new Dictionary<string, object?>
                    {
                        ["numeroexpediente"] = null
                    };
                    break;

                case "QnA":
                    reply = await HandleQnA(body, ct);
                    resets = new Dictionary<string, object?>
                    {
                        ["q"] = null
                    };
                    break;

                default:
                    reply = "Lo siento, no entendí la acción solicitada.";
                    break;
            }

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
                },
                // reseteamos parámetros
                sessionInfo = new
                {
                    parameters = resets
                }
            });
        }

        private async Task<string> HandleCrearDenuncia(FulfillmentRequest body, string? userId, CancellationToken ct)
        {
            //var p = body.sessionInfo.parameters!;
            //TODO. para pruebas sino viene el userid le asigno uno de prueba
            if (string.IsNullOrWhiteSpace(userId))
                userId = "user-123";
            Console.WriteLine("UserId: " + userId);
            var p = ParamsInsensitive(body);
            // TODO.para pruebas si no viene el sessionId le asigno uno de prueba
            var sessionId= p.ContainsKey("sessionId") && Guid.TryParse(p["sessionId"]?.ToString(), out var sid)
                ? sid
                : Guid.TryParse("5800ab38-a354-485a-88ac-61676be9b535", out var tsid) ? tsid : Guid.Empty;

            var dto = new DTO.Denuncias.CreateDenunciaDto
            {
                SessionId = sessionId,
                Nombre = p["nombre"].ToString()!,
                Cedula = p["cedula"].ToString()!,
                Ubicacion = p["ubicacion"].ToString()!,
                Descripcion = p["descripcion"].ToString()!
            };
            Console.WriteLine("Crear denuncia: " + System.Text.Json.JsonSerializer.Serialize(dto));



            var resp = await _denuncias.CreateAsync(dto, ct);
            return $"Denuncia #{resp.Id} creada correctamente.";
        }

        private async Task<string> HandleConsultarExpediente(FulfillmentRequest body, CancellationToken ct)
        {
            //var p = body.sessionInfo.parameters!;
            var p = ParamsInsensitive(body);
            var numero = p["numeroExpediente"].ToString()!;
            Console.WriteLine("Consultar expediente: " + numero);
            var resp = await _expedientes.GetByNumeroAsync(numero, ct);
            Console.WriteLine("Respuesta: " + (resp == null ? "NULL" : resp.Estado));
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



        private static IDictionary<string, object> ParamsInsensitive(FulfillmentRequest body)
        {
            return new Dictionary<string, object>(
                body.sessionInfo?.parameters ?? new(),
                StringComparer.OrdinalIgnoreCase
            );
        }

    }
}
