using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc;

namespace BotApp.Filters
{
    public class CxApiKeyFilter : IAsyncActionFilter
    {
        private readonly string _apiKey;
        public CxApiKeyFilter(IConfiguration cfg)
            => _apiKey = cfg["Cx:WebhookApiKey"] ?? throw new("Cx:WebhookApiKey missing");

        public Task OnActionExecutionAsync(ActionExecutingContext ctx, ActionExecutionDelegate next)
        {
            var key = ctx.HttpContext.Request.Headers["X-Api-Key"].ToString();
            Console.WriteLine($"CxApiKeyFilter: Received key '{key}'");
            Console.WriteLine($"CxApiKeyFilter: Expected key '{_apiKey}'");
            if (string.IsNullOrEmpty(key) || key != _apiKey)
            {
                ctx.Result = new UnauthorizedResult();
                return Task.CompletedTask;
            }
            return next();
        }
    }
}
