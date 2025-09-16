using BotApp.Models;
using BotApp.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace BotApp.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AskController : ControllerBase
    {
        private readonly ISearchService _search;
        private readonly IGeminiService _gemini;

        public AskController(ISearchService search, IGeminiService gemini)
        {
            _search = search;
            _gemini = gemini;
        }

        [HttpPost]
        public async Task<ActionResult<AskResponse>> Ask([FromBody] string question, CancellationToken ct)
        {
            var ctx = await _search.RetrieveAsync(question, ct);
            var answer = await _gemini.AnswerAsync(question, ctx, ct);
            return Ok(new AskResponse(question, answer, ctx.ToList()));
        }
    }
}
