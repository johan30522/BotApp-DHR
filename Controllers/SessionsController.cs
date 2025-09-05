using BotApp.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace BotApp.Controllers
{
    [Route("sessions")]
    [ApiController]
    public class SessionsController : ControllerBase
    {
        private readonly BotDbContext _db;
        public SessionsController(BotDbContext db) => _db = db;
        [HttpGet]
        public async Task<IActionResult> Get([FromQuery] int take = 20)
        {
            var list = await _db.Sessions
                .OrderByDescending(s => s.LastActivityUtc)
                .Take(Math.Clamp(take, 1, 100))
                .Select(s => new {
                    s.Id,
                    s.Channel,
                    s.ChannelUserId,
                    s.CxSessionPath,
                    s.CreatedAtUtc,
                    s.LastActivityUtc
                })
                .ToListAsync();

            return Ok(list);
        }

        [HttpGet("{id:guid}/messages")]
        public async Task<IActionResult> Timeline(Guid id, [FromQuery] int take = 50)
        {
            var session = await _db.Sessions.FirstOrDefaultAsync(s => s.Id == id);
            if (session == null) return NotFound();

            var msgs = await _db.Messages
                .Where(m => m.SessionId == id)
                .OrderBy(m => m.CreatedAtUtc)
                .Take(Math.Clamp(take, 1, 500))
                .Select(m => new { m.Id, m.Direction, m.Text, m.CreatedAtUtc })
                .ToListAsync();

            return Ok(new
            {
                session = new
                {
                    session.Id,
                    session.Channel,
                    session.ChannelUserId,
                    session.CxSessionPath,
                    session.CreatedAtUtc,
                    session.LastActivityUtc
                },
                messages = msgs
            });
        }
    }
}
