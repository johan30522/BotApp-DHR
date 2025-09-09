using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.WebUtilities;
namespace BotApp.Filters
{
    public class TwilioSignatureFilter
    {
        private readonly string _authToken;
        private readonly IConfiguration _cfg;
        public TwilioSignatureFilter(IConfiguration cfg)
        {
            _cfg = cfg;
            _authToken = cfg["Twilio:AuthToken"] ?? throw new InvalidOperationException("Twilio:AuthToken requerido");
        }

        public async Task OnActionExecutionAsync(ActionExecutingContext ctx, ActionExecutionDelegate next)
        {
            var headerSig = ctx.HttpContext.Request.Headers["X-Twilio-Signature"].ToString();
            if (string.IsNullOrEmpty(headerSig)) { ctx.Result = new ForbidResult(); return; }

            // Twilio firma: URL absoluto + params form (orden alfabético)
            var req = ctx.HttpContext.Request;
            var rawUrl = $"{req.Scheme}://{req.Host}{req.Path}{req.QueryString}";
            // si usás un public URL detrás de un LB, setéalo en config:
            var publicUrl = _cfg["Twilio:PublicWebhookUrl"] ?? rawUrl;

            ctx.HttpContext.Request.EnableBuffering();
            string formBody;
            using (var reader = new StreamReader(req.Body, Encoding.UTF8, leaveOpen: true))
            {
                formBody = await reader.ReadToEndAsync();
                req.Body.Position = 0;
            }
            var form = QueryHelpers.ParseQuery(formBody.Replace("&amp;", "&"));
            var sb = new StringBuilder(publicUrl);
            foreach (var kv in form.OrderBy(k => k.Key, StringComparer.Ordinal))
                sb.Append(kv.Key).Append(kv.Value);

            using var h = new HMACSHA1(Encoding.UTF8.GetBytes(_authToken));
            var hash = h.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
            var calc = Convert.ToBase64String(hash);

            if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(calc), Encoding.UTF8.GetBytes(headerSig)))
            {
                ctx.Result = new ForbidResult(); return;
            }

            await next();
        }
    }
}
