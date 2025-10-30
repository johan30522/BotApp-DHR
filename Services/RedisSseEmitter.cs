using System.Text.Json;
using StackExchange.Redis;
using BotApp.Contracts;



namespace BotApp.Services
{
    public interface ISseEmitter
    {
        Task EmitAck(string sessionId, string turnId, string text, object? meta = null);
        Task EmitProgress(string sessionId, string turnId, string textOrDelta, object? meta = null);
        Task EmitFinal(string sessionId, string turnId, string text, object? meta = null);
        Task EmitTool(string sessionId, string turnId, string tool, string status, object? progress = null);
        Task EmitDone(string sessionId, string turnId);
        Task EmitError(string sessionId, string turnId, string code, string message, bool retryable);
    }
    public sealed class RedisSseEmitter : ISseEmitter
    {
        private readonly IConnectionMultiplexer _redis;
        private readonly string _channelPrefix;
        private static readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web);

        public RedisSseEmitter(IConnectionMultiplexer redis, IConfiguration cfg)
        {
            _redis = redis;
            _channelPrefix = cfg["SSE:ChannelPrefix"] ?? "sse:";
        }

        private ISubscriber Bus => _redis.GetSubscriber();
        private string Chan(string sessionId) => $"{_channelPrefix}{sessionId}";

        private Task Pub(string sessionId, object evt)
            => Bus.PublishAsync(Chan(sessionId), JsonSerializer.Serialize(evt, _json));

        public async Task EmitAck(string s, string t, string text, object? meta = null)
        {
            var evt = new SseMessageEvent(s, t, "assistant", "ack", text, meta, DateTime.UtcNow);
            await Pub(s, evt);
            await SaveSnapshot(s, t, evt);
        }

        public async Task EmitProgress(string s, string t, string textOrDelta, object? meta = null)
        {
            var evt = new SseMessageEvent(s, t, "assistant", "progress", textOrDelta, meta, DateTime.UtcNow);
            await Pub(s, evt);
            await SaveSnapshot(s, t, evt);
        }
        public async Task EmitFinal(string s, string t, string text, object? meta = null)
        {
            var evt = new SseMessageEvent(s, t, "assistant", "final", text, meta, DateTime.UtcNow);
            await Pub(s, evt);
            await SaveSnapshot(s, t, evt);

        }
        public async Task EmitTool(string s, string t, string tool, string status, object? progress = null)
        {
            var evt = new SseToolEvent(s, t, tool, status, progress, DateTime.UtcNow);
            await Pub(s, evt);
            await SaveSnapshot(s, t, evt);
        }

        public async Task EmitDone(string s, string t)
        {
            var evt = new SseDoneEvent(s, t, DateTime.UtcNow);
            await Pub(s, evt);
            await SaveSnapshot(s, t, evt);
        }

        public async Task EmitError(string s, string t, string code, string message, bool retryable)
        {
            var evt = new SseErrorEvent(s, t, code, message, retryable, DateTime.UtcNow);
            await Pub(s, evt);
            await SaveSnapshot(s, t, evt);
        }
        private Task SaveSnapshot(string sessionId, string turnId, object evt)
        {
            var db = _redis.GetDatabase();
            var key = $"sse:last:{sessionId}:{turnId}";
            var json = JsonSerializer.Serialize(evt, _json);
            return db.StringSetAsync(key, json, expiry: TimeSpan.FromMinutes(20));
        }
    }
}
