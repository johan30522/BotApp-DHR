using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace BotApp.Controllers
{
    [Route("health")]
    [ApiController]
    public class HealthController : ControllerBase
    {
        [HttpGet]
        public IActionResult Get() => Ok(new { ok = true, ts = DateTime.UtcNow });
    }
}
