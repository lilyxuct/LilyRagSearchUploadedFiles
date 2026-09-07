using LilyRagPractices.Services;
using Microsoft.AspNetCore.Mvc;
using UglyToad.PdfPig;

namespace LilyRagPractices.Controller
{
    [ApiController]
    [Route("api/upload")]
    public class UploadController : ControllerBase
    {
        private readonly DocumentService _docs;
        private readonly EmbeddingService _embed;

        public UploadController(DocumentService docs, EmbeddingService embed)
        {
            _docs = docs;
            _embed = embed;
        }

        [HttpPost]
        public async Task<IActionResult> Upload(IFormFile file)
        {
            var content = (await ExtractTextAsync(file)).Replace("\0", string.Empty);
            if (string.IsNullOrWhiteSpace(content))
            {
                return BadRequest("Could not extract text from uploaded file.");
            }

            var chunks = SplitIntoChunks(content, 800, 120);
            if (chunks.Count == 0)
            {
                return BadRequest("No usable text chunks were produced from uploaded file.");
            }

            foreach (var chunk in chunks)
            {
                var embedding = await _embed.GetEmbeddingAsync(chunk);
                await _docs.InsertDocumentAsync(file.FileName, chunk, embedding);
            }

            return Ok(new { message = "Uploaded and indexed", chunks = chunks.Count });
        }

        private static List<string> SplitIntoChunks(string text, int chunkSize, int overlap)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(text))
            {
                return result;
            }

            var step = Math.Max(1, chunkSize - overlap);
            for (int i = 0; i < text.Length; i += step)
            {
                var len = Math.Min(chunkSize, text.Length - i);
                var chunk = text.Substring(i, len).Trim();
                if (!string.IsNullOrWhiteSpace(chunk))
                {
                    result.Add(chunk);
                }
            }

            return result;
        }

        private static async Task<string> ExtractTextAsync(IFormFile file)
        {
            if (file.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            {
                await using var uploadStream = file.OpenReadStream();
                using var memory = new MemoryStream();
                await uploadStream.CopyToAsync(memory);
                memory.Position = 0;

                using var pdf = UglyToad.PdfPig.PdfDocument.Open(memory);
                var sb = new System.Text.StringBuilder();
                foreach (var page in pdf.GetPages())
                {
                    sb.AppendLine(page.Text);
                }

                return sb.ToString();
            }

            using var reader = new StreamReader(file.OpenReadStream());
            return await reader.ReadToEndAsync();
        }
    }

}
