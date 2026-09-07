using LilyRagPractices.Services;
using Microsoft.AspNetCore.Mvc;

namespace LilyRagPractices.Controller
{
    [ApiController]
    [Route("api/query")]
    public class QueryController : ControllerBase
    {
        private readonly DocumentService _docs;
        private readonly EmbeddingService _embed;

        public QueryController(DocumentService docs, EmbeddingService embed)
        {
            _docs = docs;
            _embed = embed;
        }

        [HttpPost]
        public async Task<IActionResult> Query([FromBody] QueryRequest req)
        {
            var queryEmbedding = await _embed.GetEmbeddingAsync(req.Question);

            var results = await _docs.SearchAsync(queryEmbedding);

            return Ok(results);
        }
    }

    public class QueryRequest
    {
        public required string Question { get; set; }
    }


}
