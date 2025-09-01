using BotApp.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace BotApp.Controllers
{
    [Route("cx/fulfillment")]
    [ApiController]
    public class CxFulfillmentController : ControllerBase
    {
        private readonly IConfiguration _cfg;
        public CxFulfillmentController(IConfiguration cfg) => _cfg = cfg;

        [HttpPost]
        public IActionResult Post()
        {
            var expected = _cfg["Cx:WebhookApiKey"];
            if (!CxApiKeyAuth.IsValid(Request, expected))
                return Unauthorized();

            return Ok(new
            {
                fulfillmentResponse = new
                {
                    messages = new[] {
                    new { text = new { text = new[] { "Webhook OK (stub)" } } }
                }
                }
            });
        }
    }
}
