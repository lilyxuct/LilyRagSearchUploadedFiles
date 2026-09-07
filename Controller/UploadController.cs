using System.Text;
using System.Text.RegularExpressions;
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
        private readonly IWebHostEnvironment _env;
        private readonly IConfiguration _config;
        private readonly ILogger<UploadController> _logger;

        public UploadController(
            DocumentService docs,
            EmbeddingService embed,
            IWebHostEnvironment env,
            IConfiguration config,
            ILogger<UploadController> logger)
        {
            _docs = docs;
            _embed = embed;
            _env = env;
            _config = config;
            _logger = logger;
        }

        [HttpPost]
        public async Task<IActionResult> Upload(IFormFile file)
        {
            if (file is null || file.Length == 0)
                return BadRequest("No file uploaded.");

            var savedPath = await SaveUploadedFileAsync(file);

            string content;
            try
            {
                content = (await ExtractTextAsync(file)).Replace("\0", string.Empty);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed extracting text from {FileName}", file.FileName);
                return BadRequest("Failed extracting text from uploaded file.");
            }

            if (string.IsNullOrWhiteSpace(content))
                return BadRequest("Could not extract text from uploaded file.");

            var chunks = SplitIntoChunks(content, 800, 120);
            if (chunks.Count == 0)
                return BadRequest("No usable text chunks were produced from uploaded file.");

            var savedFileName = Path.GetFileName(savedPath);

            var inserted = 0;
            var failed = 0;

            for (var i = 0; i < chunks.Count; i++)
            {
                var chunk = chunks[i];
                _logger.LogInformation("Processing chunk {Index}/{Total} for {File}", i + 1, chunks.Count, savedFileName);

                using var cts = CancellationTokenSource.CreateLinkedTokenSource(HttpContext.RequestAborted);
                cts.CancelAfter(TimeSpan.FromSeconds(25));

                try
                {
                    var embedding = await _embed.GetEmbeddingAsync(chunk, cts.Token);
                    await _docs.InsertDocumentAsync(savedFileName, chunk, embedding);
                    inserted++;
                }
                catch (OperationCanceledException)
                {
                    failed++;
                    _logger.LogWarning("Chunk {Index} timed out for {File}", i + 1, savedFileName);
                }
                catch (Exception ex)
                {
                    failed++;
                    _logger.LogError(ex, "Chunk {Index} failed for {File}", i + 1, savedFileName);
                }
            }

            return Ok(new
            {
                message = failed == 0 ? "Uploaded, saved, and indexed" : "Uploaded with partial indexing",
                file = savedFileName,
                savedTo = savedPath,
                chunks = inserted,
                chunksTotal = chunks.Count,
                chunksFailed = failed
            });
        }

        private async Task<string> SaveUploadedFileAsync(IFormFile file)
        {
            var folder = _config["Ingestion:DocumentsFolder"];
            if (string.IsNullOrWhiteSpace(folder))
                folder = Path.Combine(_env.ContentRootPath, "documents");

            Directory.CreateDirectory(folder);

            var originalName = Path.GetFileName(file.FileName);
            var targetPath = Path.Combine(folder, originalName);

            if (System.IO.File.Exists(targetPath))
            {
                var baseName = Path.GetFileNameWithoutExtension(originalName);
                var ext = Path.GetExtension(originalName);
                var uniqueName = $"{baseName}_{DateTime.UtcNow:yyyyMMddHHmmssfff}{ext}";
                targetPath = Path.Combine(folder, uniqueName);
            }

            await using var fs = System.IO.File.Create(targetPath);
            await file.CopyToAsync(fs);

            return targetPath;
        }

        private static List<string> SplitIntoChunks(string text, int chunkSize, int overlap)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(text) || chunkSize <= 0) return result;

            if (overlap < 0) overlap = 0;
            if (overlap >= chunkSize) overlap = Math.Max(1, chunkSize / 4);

            text = NormalizeTextForChunking(text);

            var start = 0;
            var safety = 0;
            var maxSafety = Math.Max(10_000, text.Length * 2);

            while (start < text.Length)
            {
                if (++safety > maxSafety) break;

                var previousStart = start;
                var maxEnd = Math.Min(start + chunkSize, text.Length);
                var end = FindBestBreak(text, start, maxEnd, chunkSize);

                if (end <= start) end = maxEnd;

                var chunk = text[start..end].Trim();
                if (!string.IsNullOrWhiteSpace(chunk))
                    result.Add(chunk);

                if (end >= text.Length) break;

                var nextStart = Math.Max(0, end - overlap);
                nextStart = MoveToNextWordBoundary(text, nextStart);

                if (nextStart <= previousStart)
                    nextStart = Math.Min(text.Length, previousStart + Math.Max(1, chunkSize - overlap));

                start = nextStart;
            }

            return result;
        }

        private static int FindBestBreak(string text, int start, int maxEnd, int chunkSize)
        {
            var minPreferred = start + Math.Max(1, chunkSize / 2);
            minPreferred = Math.Min(minPreferred, maxEnd);

            for (var i = maxEnd - 1; i >= minPreferred; i--)
            {
                if (IsSentenceBoundary(text[i])) return i + 1;
            }

            for (var i = maxEnd - 1; i > start; i--)
            {
                if (char.IsWhiteSpace(text[i])) return i + 1;
            }

            return maxEnd;
        }

        private static bool IsSentenceBoundary(char c) =>
            c == '.' || c == '!' || c == '?' || c == ';' || c == ':';

        private static int MoveToNextWordBoundary(string text, int index)
        {
            if (index <= 0) return 0;

            while (index < text.Length &&
                   index > 0 &&
                   char.IsLetterOrDigit(text[index]) &&
                   char.IsLetterOrDigit(text[index - 1]))
            {
                index++;
            }

            while (index < text.Length && char.IsWhiteSpace(text[index]))
                index++;

            return index;
        }

        private static string NormalizeTextForChunking(string text)
        {
            text = text.Replace("\r\n", "\n").Replace('\r', '\n');
            text = Regex.Replace(text, @"-\s*\n\s*", string.Empty);
            text = Regex.Replace(text, @"\s*\n\s*", " ");
            text = Regex.Replace(text, @"\s+", " ").Trim();
            return text;
        }

        private static async Task<string> ExtractTextAsync(IFormFile file)
        {
            if (file.FileName.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            {
                await using var uploadStream = file.OpenReadStream();
                using var memory = new MemoryStream();
                await uploadStream.CopyToAsync(memory);
                memory.Position = 0;

                using var pdf = PdfDocument.Open(memory);
                var sb = new StringBuilder();
                foreach (var page in pdf.GetPages())
                    sb.AppendLine(page.Text);

                return sb.ToString();
            }

            using var reader = new StreamReader(file.OpenReadStream());
            return await reader.ReadToEndAsync();
        }
    }
}