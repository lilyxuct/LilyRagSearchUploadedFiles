using LilyRagPractices.Services;
using Microsoft.AspNetCore.Mvc;

using System.Text;
using LilyRagPractices.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.AI;

namespace LilyRagPractices.Controller
{
    [ApiController]
    [Route("api/query")]
    public class QueryController : ControllerBase
    {
        private readonly DocumentService _docs;
        private readonly EmbeddingService _embed;
        private readonly IChatClient _chat;

        public QueryController(DocumentService docs, EmbeddingService embed, IChatClient chat)
        {
            _docs = docs;
            _embed = embed;
            _chat = chat;
        }

        [HttpPost]
        public async Task<IActionResult> Query([FromBody] QueryRequest req)
        {
            if (string.IsNullOrWhiteSpace(req.Question))
            {
                return BadRequest("Question is required.");
            }

            var queryEmbedding = await _embed.GetEmbeddingAsync(req.Question);
            var results = await _docs.SearchAsync(queryEmbedding);

            if (results.Count == 0)
            {
                return Ok(new QueryResponse
                {
                    Answer = "I could not find relevant information in the indexed documents.",
                    Sources = []
                });
            }

            var contextBuilder = new StringBuilder();
            for (var i = 0; i < results.Count; i++)
            {
                contextBuilder.AppendLine($"[{i + 1}] (score: {results[i].Score:F3})");
                contextBuilder.AppendLine(results[i].Content);
                contextBuilder.AppendLine();
            }

            var prompt = $"""
                          You are a helpful assistant answering questions using retrieved document chunks.

                          Rules:
                          - Use only the context below.
                          - If the context is insufficient, say so clearly.
                          - Keep the answer concise and factual.

                          Question:
                          {req.Question}

                          Context:
                          {contextBuilder}
                          """;

            var llm = await _chat.GetResponseAsync(prompt);
            var answer = string.IsNullOrWhiteSpace(llm.Text) ? llm.ToString() : llm.Text;

            return Ok(new QueryResponse
            {
                Answer = answer,
                Sources = results
            });
        }
    }

    public class QueryRequest
    {
        public required string Question { get; set; }
    }

    public sealed class QueryResponse
    {
        public string Answer { get; set; } = string.Empty;
        public List<DocumentService.SearchResult> Sources { get; set; } = [];
    }
}