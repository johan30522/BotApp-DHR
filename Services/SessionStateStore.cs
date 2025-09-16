using StackExchange.Redis;
using System.Text.Json;

namespace BotApp.Services
{
    public class SessionStateStore
    {
        private readonly IDatabase _db;
        private static readonly JsonSerializerOptions J = new(JsonSerializerDefaults.Web);

        // TTLs sugeridos (ajustá a tu gusto)
        private static readonly TimeSpan SESS_TTL = TimeSpan.FromMinutes(45); // estado vivo
        private static readonly TimeSpan QNA_TTL = TimeSpan.FromHours(2);    // memoria corta QnA
        private static readonly TimeSpan IDEM_TTL = TimeSpan.FromHours(24);   // idempotencia (p/ WhatsApp)
        private static readonly TimeSpan LOCK_TTL = TimeSpan.FromSeconds(8);  // locks cortos

        // Limites de historia QnA
        private const int QNA_MAX = 5;

        public SessionStateStore(IConnectionMultiplexer mux) => _db = mux.GetDatabase();

        // ---------- Namespacing de claves ----------
        private static string SKey(Guid sid) => $"session:{sid}:state";
        private static string QnaKey(Guid sid) => $"qna:{sid}:history";
        private static string IdemKey(string p, string id) => $"idem:{p}:{id}";
        private static string LockKey(string name) => $"lock:{name}";
        private static string CacheKey(string ns, string k) => $"cache:{ns}:{k}";

        // ---------- Estado vivo (JSON por sesión) ----------
        public async Task<T> GetAsync<T>(Guid sid) where T : class, new()
        {
            var raw = await _db.StringGetAsync(SKey(sid));
            if (!raw.HasValue) return new T();
            try { return JsonSerializer.Deserialize<T>(raw!, J) ?? new T(); }
            catch { return new T(); }
        }

        public Task SetAsync<T>(Guid sid, T value)
        {
            var json = JsonSerializer.Serialize(value, J);
            return _db.StringSetAsync(SKey(sid), json, SESS_TTL);
        }

        public Task<bool> DeleteSessionAsync(Guid sid) => _db.KeyDeleteAsync(SKey(sid));

        // ---------- QnA: memoria corta por sesión ----------
        public async Task AddQnaAsync(Guid sid, QnaExchange ex)
        {
            var key = QnaKey(sid);
            var json = JsonSerializer.Serialize(ex, J);
            var tran = _db.CreateTransaction();
            _ = tran.ListLeftPushAsync(key, json);
            _ = tran.ListTrimAsync(key, 0, QNA_MAX - 1);
            _ = tran.KeyExpireAsync(key, QNA_TTL);
            await tran.ExecuteAsync();
        }

        public async Task<List<QnaExchange>> GetRecentQnaAsync(Guid sid, int take = 3)
        {
            var key = QnaKey(sid);
            var vals = await _db.ListRangeAsync(key, 0, take - 1);
            var list = new List<QnaExchange>(vals.Length);
            foreach (var v in vals)
            {
                if (!v.HasValue) continue;
                try { list.Add(JsonSerializer.Deserialize<QnaExchange>(v!, J)!); }
                catch { /* ignore */ }
            }
            return list;
        }

        public async Task<QnaExchange?> GetLastQnaAsync(Guid sid)
            => (await GetRecentQnaAsync(sid, 1)).FirstOrDefault();

        public Task<bool> ClearQnaAsync(Guid sid) => _db.KeyDeleteAsync(QnaKey(sid));

        // ---------- Idempotencia (e.g., WhatsApp) ----------
        // Devuelve true si es la primera vez (marca creada); false si ya existía (duplicado)
        public Task<bool> EnsureIdempotentAsync(string provider, string messageId, TimeSpan? ttl = null)
            => _db.StringSetAsync(IdemKey(provider, messageId), "1", ttl ?? IDEM_TTL, When.NotExists);

        // ---------- Locks ligeros ----------
        // Devuelve true si tomó el lock; false si ya estaba tomado
        public Task<bool> AcquireLockAsync(string name, TimeSpan? ttl = null)
            => _db.StringSetAsync(LockKey(name), "1", ttl ?? LOCK_TTL, When.NotExists);

        public Task ReleaseLockAsync(string name) => _db.KeyDeleteAsync(LockKey(name));

        // ---------- Caché corta (key/value string) ----------
        public Task<bool> CacheSetAsync(string ns, string key, string value, TimeSpan ttl)
            => _db.StringSetAsync(CacheKey(ns, key), value, ttl);

        public async Task<string?> CacheGetAsync(string ns, string key)
        {
            var v = await _db.StringGetAsync(CacheKey(ns, key));
            return v.HasValue ? v.ToString() : null;
        }

        public Task<bool> CacheDelAsync(string ns, string key)
            => _db.KeyDeleteAsync(CacheKey(ns, key));
    }

    public class QnaExchange
    {
        public string q { get; set; } = default!;
        public string a { get; set; } = default!;
        public DateTime at { get; set; } = DateTime.UtcNow;
        public List<string>? refs { get; set; }
    }
}
