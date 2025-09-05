using BotApp.Data;
using BotApp.Models;
using Microsoft.EntityFrameworkCore;

namespace BotApp.Services
{
    public class SessionService
    {
        private readonly BotDbContext _db;
        public SessionService(BotDbContext db)
        {
            _db = db;
        }
        public async Task<Session> GetOrCreateAsync(string channel, string channelUserId, string? cxSessionPath = null, CancellationToken ct = default)
        {
            var s = await _db.Sessions
                .FirstOrDefaultAsync(x => x.Channel == channel && x.ChannelUserId == channelUserId, ct);

            if (s is null)
            {
                s = new Session
                {
                    Channel = channel,
                    ChannelUserId = channelUserId,
                    CxSessionPath = cxSessionPath ?? $"local/{Guid.NewGuid()}",
                    CreatedAtUtc = DateTime.UtcNow,
                    LastActivityUtc = DateTime.UtcNow
                };
                _db.Sessions.Add(s);
                await _db.SaveChangesAsync(ct);
            }
            else
            {
                s.LastActivityUtc = DateTime.UtcNow;
                await _db.SaveChangesAsync(ct);
            }

            return s;
        }

        public async Task<Message> AddIncomingMessageAsync(Guid sessionId, string? text, string? payloadJson, string? channelMessageId = null, CancellationToken ct = default)
        {
            var msg = new Message
            {
                SessionId = sessionId,
                Direction = "in",
                Text = text,
                PayloadJson = payloadJson,
                ChannelMessageId = channelMessageId,
                CreatedAtUtc = DateTime.UtcNow
            };
            _db.Messages.Add(msg);
            await _db.SaveChangesAsync(ct);
            return msg;
        }

        public async Task AddEventAsync(Guid? sessionId, string type, string result = "ok", int? elapsedMs = null, string? dataJson = null, CancellationToken ct = default)
        {
            _db.Events.Add(new EventLog
            {
                SessionId = sessionId,
                Type = type,
                ElapsedMs = elapsedMs,
                PayloadJson = dataJson,
                CreatedAtUtc = DateTime.UtcNow
            });
            await _db.SaveChangesAsync(ct);
        }
    }
}
