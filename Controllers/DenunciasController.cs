using BotApp.DTO.Denuncias;
using BotApp.Models;
using BotApp.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace BotApp.Controllers
{
    [Route("denuncias")]
    [ApiController]
    public class DenunciasController : ControllerBase
    {
        private const string StartPagePath =
        "projects/chatbot-empresa-468919/locations/us-central1/agents/68269666-1efa-4aef-8ab6-25d614e8b1b4/flows/00000000-0000-0000-0000-000000000000/pages/START_PAGE";
        private static readonly HashSet<string> CancelWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "cancelar","cancela","cancelá","cancele",
            "detén","detener","parar","stop",
            "olvídalo","olvidalo","mejor no","no seguir","no quiero seguir"
        };


        [HttpPost("ValidateParam")]
        public IActionResult ValidateParam([FromBody] DfcxRequest body)
        {
            Console.WriteLine("Se ejecuta el validador");

            Console.WriteLine("🟦 body.text: " + (body.text ?? "NULL"));
            // Log útil para depurar
            var pinfo = body?.pageInfo?.formInfo?.parameterInfo ?? new List<ParameterInfo>();
            Console.WriteLine("🟦 parameterInfo: " + System.Text.Json.JsonSerializer.Serialize(
                pinfo, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));

            if (pinfo.Count == 0)
            {
                Console.WriteLine("Sale. pinfo.Count == 0");
                return Ok(new { });
            }

            // 1) ¿Cuál es el parámetro relevante en este turno?
            //    Prioriza el que tenga justCollected == true; si no aparece,
            //    toma el primer EMPTY (el siguiente que el bot está pidiendo).
            var active =
                pinfo.FirstOrDefault(p => p.justCollected == true) ??
                pinfo.FirstOrDefault(p => string.Equals(p.state, "EMPTY", StringComparison.OrdinalIgnoreCase));

            if (active == null || string.IsNullOrWhiteSpace(active.displayName))
                return Ok(new { });

            // 2) Valor candidato: preferimos sessionInfo.parameters; si no, parameterInfo.value.
            string candidate = null;

            if (body.sessionInfo?.parameters != null &&
                body.sessionInfo.parameters.TryGetValue(active.displayName, out var raw))
            {
                candidate = raw?.ToString()?.Trim();
            }
            if (string.IsNullOrWhiteSpace(candidate) && active.value != null)
            {
                candidate = active.value.ToString().Trim();
            }
            if (string.IsNullOrWhiteSpace(candidate) && !string.IsNullOrWhiteSpace(body.text))
            {
                candidate = body.text.Trim();
            }

            Console.WriteLine($"candidate final para {active.displayName}: {candidate ?? "NULL"}");

            // 3) ¿Es una cancelación?
            if (!string.IsNullOrEmpty(candidate) && CancelWords.Contains(candidate))
            {

                var clear = new Dictionary<string, object>
                {
                    ["nombreDenunciante"] = null,
                    ["numeroCedula"] = null,
                    ["descripcionDenuncia"] = null,
                    ["lugarDenuncia"] = null,
                    ["cancel"] = true
                };

                var msg = "Cancelé la creación de tu denuncia. Si querés, podemos empezar de nuevo o hacer otra consulta.";

                return Ok(new
                {
                    sessionInfo = new { parameters = clear },
                    fulfillmentResponse = new
                    {
                        mergeBehavior = "REPLACE",
                        messages = new object[]
                        {
                        new { text = new { text = new[] { msg } } }
                        }
                    },
                    // Usa tu ID completo de Start Page
                    targetPage = StartPagePath
                });
            }
            Console.WriteLine($"No hay Cancelacion");
            // 4) Si no hay cancelación, deja seguir el llenado.
            return Ok(new { });
        }
    }
}
