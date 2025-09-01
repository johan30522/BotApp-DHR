using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using BotApp.Services;
using BotApp.Data;
using Microsoft.EntityFrameworkCore;

namespace BotApp.Controllers
{
    [Route("diagnostics")]
    [ApiController]
    public class DiagnosticsController : ControllerBase
    {
        private readonly RedisClient _redis;
        private readonly BotDbContext _db;

        public DiagnosticsController(RedisClient redis, BotDbContext db)
        {
            _redis = redis;
            _db = db;
        }

        [HttpGet("redis")]
        public async Task<IActionResult> Redis() =>
            Ok(new { pingMs = await _redis.PingAsync() });

        [HttpGet("sql")]
        public async Task<IActionResult> Sql()
        {
            await using var conn = _db.Database.GetDbConnection();
            await conn.OpenAsync();
            await using var cmd = conn.CreateCommand();
            cmd.CommandText = "select now()";
            var now = await cmd.ExecuteScalarAsync();
            return Ok(new { now });
        }
    }
}
