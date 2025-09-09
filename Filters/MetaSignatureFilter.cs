// Security/MetaSignatureFilter.cs
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System.Security.Cryptography;
using System.Text;

namespace BotApp.Filters
{
    public class MetaSignatureFilter : IAsyncActionFilter
    {
        private readonly string _appSecret;
        public MetaSignatureFilter(IConfiguration cfg)
        {
            _appSecret = cfg["Meta:AppSecret"] ?? throw new InvalidOperationException("Meta:AppSecret requerido");
        }

        public async Task OnActionExecutionAsync(ActionExecutingContext ctx, ActionExecutionDelegate next)
        {
            var sig = ctx.HttpContext.Request.Headers["X-Hub-Signature-256"].ToString();
            if (string.IsNullOrEmpty(sig) || !sig.StartsWith("sha256=", StringComparison.OrdinalIgnoreCase))
            {
                ctx.Result = new ForbidResult(); return;
            }

            ctx.HttpContext.Request.EnableBuffering();
            using var reader = new StreamReader(ctx.HttpContext.Request.Body, Encoding.UTF8, leaveOpen: true);
            var body = await reader.ReadToEndAsync();
            ctx.HttpContext.Request.Body.Position = 0;

            using var h = new HMACSHA256(Encoding.UTF8.GetBytes(_appSecret));
            var hash = h.ComputeHash(Encoding.UTF8.GetBytes(body));
            var calc = "sha256=" + Convert.ToHexString(hash).ToLowerInvariant();

            if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(calc), Encoding.UTF8.GetBytes(sig)))
            {
                ctx.Result = new ForbidResult(); return;
            }

            await next();
        }
    }
}
