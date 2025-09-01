using StackExchange.Redis;

namespace BotApp.Services
{
    public class RedisClient
    {
        private readonly string _host;
        private readonly int _port;

        public RedisClient(IConfiguration cfg)
        {
            _host = cfg["Redis:Host"] ?? "127.0.0.1";
            _port = int.TryParse(cfg["Redis:Port"], out var p) ? p : 6379;
        }

        public async Task<double> PingAsync()
        {
            var mux = await ConnectionMultiplexer.ConnectAsync($"{_host}:{_port}");
            var pong = await mux.GetDatabase().PingAsync();
            return pong.TotalMilliseconds;
        }
    }
}
