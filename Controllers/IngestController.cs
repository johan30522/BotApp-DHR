using BotApp.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace BotApp.Controllers
{
    [Route("ingest")]
    [ApiController]
    public class IngestController : ControllerBase
    {
        [HttpPost("web")]
        public IActionResult Web([FromBody] UniMessage msg) =>
        Ok(new { accepted = true, channel = "web" });

        [HttpPost("whatsapp")]
        public IActionResult WhatsApp([FromBody] UniMessage msg) =>
            Ok(new { accepted = true, channel = "whatsapp" });
    }
}
