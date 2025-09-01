namespace BotApp.Services
{
    public class CxApiKeyAuth
    {
        public const string HeaderName = "X-Api-Key";
        public static bool IsValid(HttpRequest req, string? expectedKey) =>
            !string.IsNullOrWhiteSpace(expectedKey) &&
            req.Headers.TryGetValue(HeaderName, out var got) && got == expectedKey;
    }
}
