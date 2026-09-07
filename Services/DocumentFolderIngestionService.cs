using System.Text;
using System.Text.RegularExpressions;
using Npgsql;
using UglyToad.PdfPig;

namespace LilyRagPractices.Services
{
    public sealed class DocumentFolderIngestionService : BackgroundService
    {
        private readonly ILogger<DocumentFolderIngestionService> _logger;
        private readonly IWebHostEnvironment _env;
        private readonly IConfiguration _config;
        private readonly DocumentService _docs;
        private readonly EmbeddingService _embed;

        public DocumentFolderIngestionService(
            ILogger<DocumentFolderIngestionService> logger,
            IWebHostEnvironment env,
            IConfiguration config,
            DocumentService docs,
            EmbeddingService embed)
        {
            _logger = logger;
            _env = env;
            _config = config;
            _docs = docs;
            _embed = embed;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var runOnStartup = _config.GetValue("Ingestion:RunOnStartup", true);
            if (!runOnStartup)
            {
                _logger.LogInformation("Document ingestion is disabled.");
                return;
            }

            var forceReindex = _config.GetValue("Ingestion:ForceReindexOnStartup", false);

            var folder = _config["Ingestion:DocumentsFolder"];
            if (string.IsNullOrWhiteSpace(folder))
            {
                folder = Path.Combine(_env.ContentRootPath, "documents");
            }

            _logger.LogInformation("Ingestion started. Folder: {Folder}. ForceReindex: {ForceReindex}", folder, forceReindex);

            if (!Directory.Exists(folder))
            {
                _logger.LogWarning("Documents folder not found: {Folder}", folder);
                return;
            }

            var pdfFiles = Directory
                .EnumerateFiles(folder, "*.*", SearchOption.TopDirectoryOnly)
                .Where(f => string.Equals(Path.GetExtension(f), ".pdf", StringComparison.OrdinalIgnoreCase))
                .ToList();

            _logger.LogInformation("Found {Count} PDF files in {Folder}.", pdfFiles.Count, folder);

            var indexedFiles = 0;
            var failedFiles = 0;
            var skippedFiles = 0;
            var totalChunksInserted = 0;

            foreach (var path in pdfFiles)
            {
                if (stoppingToken.IsCancellationRequested) break;

                var fileName = Path.GetFileName(path);

                try
                {
                    if (forceReindex)
                    {
                        await DeleteByFileNameAsync(fileName, stoppingToken);
                    }
                    else if (await _docs.IsFileIndexedAsync(fileName, stoppingToken))
                    {
                        skippedFiles++;
                        _logger.LogInformation("Skipping already indexed file: {FileName}", fileName);
                        continue;
                    }

                    var content = (await ExtractPdfTextAsync(path, stoppingToken)).Replace("\0", string.Empty);
                    if (string.IsNullOrWhiteSpace(content))
                    {
                        failedFiles++;
                        _logger.LogWarning("No text extracted from {FileName}. Skipping.", fileName);
                        continue;
                    }

                    var chunks = SplitIntoChunks(content, 800, 120);
                    if (chunks.Count == 0)
                    {
                        failedFiles++;
                        _logger.LogWarning("No chunks produced for {FileName}. Skipping.", fileName);
                        continue;
                    }

                    var inserted = 0;
                    foreach (var chunk in chunks)
                    {
                        var embedding = await _embed.GetEmbeddingAsync(chunk);
                        await _docs.InsertDocumentAsync(fileName, chunk, embedding);
                        inserted++;
                    }

                    indexedFiles++;
                    totalChunksInserted += inserted;
                    _logger.LogInformation("Indexed {FileName}: {Inserted} chunks.", fileName, inserted);
                }
                // Replace only the catch block inside foreach:
                catch (Exception ex)
                {
                    failedFiles++;
                    _logger.LogError(ex, "Failed indexing file {FileName}", fileName);
                    throw; // fail fast so you immediately see the real error
                }
            }

            _logger.LogInformation(
                "Ingestion completed. IndexedFiles={IndexedFiles}, SkippedFiles={SkippedFiles}, FailedFiles={FailedFiles}, TotalChunksInserted={TotalChunksInserted}",
                indexedFiles, skippedFiles, failedFiles, totalChunksInserted);
        }

        private async Task DeleteByFileNameAsync(string fileName, CancellationToken cancellationToken)
        {
            var connStr = _config.GetConnectionString("RagDb")
                ?? throw new InvalidOperationException("ConnectionStrings:RagDb is not configured.");

            await using var conn = new NpgsqlConnection(connStr);
            await conn.OpenAsync(cancellationToken);

            await using var cmd = new NpgsqlCommand("DELETE FROM documents WHERE file_name = @f", conn);
            cmd.Parameters.AddWithValue("f", fileName);

            var deleted = await cmd.ExecuteNonQueryAsync(cancellationToken);
            _logger.LogInformation("ForceReindex enabled. Deleted {Deleted} existing rows for {FileName}.", deleted, fileName);
        }

        private static async Task<string> ExtractPdfTextAsync(string path, CancellationToken cancellationToken)
        {
            await using var fs = File.OpenRead(path);
            using var memory = new MemoryStream();
            await fs.CopyToAsync(memory, cancellationToken);
            memory.Position = 0;

            using var pdf = PdfDocument.Open(memory);
            var sb = new StringBuilder();
            foreach (var page in pdf.GetPages())
            {
                sb.AppendLine(page.Text);
            }

            return sb.ToString();
        }

        private static List<string> SplitIntoChunks(string text, int chunkSize, int overlap)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(text) || chunkSize <= 0) return result;

            if (overlap < 0) overlap = 0;
            if (overlap >= chunkSize) overlap = Math.Max(1, chunkSize / 4);

            text = NormalizeTextForChunking(text);

            var start = 0;
            while (start < text.Length)
            {
                var maxEnd = Math.Min(start + chunkSize, text.Length);
                var end = FindBestBreak(text, start, maxEnd, chunkSize);
                if (end <= start) end = maxEnd;

                var chunk = text[start..end].Trim();
                if (!string.IsNullOrWhiteSpace(chunk)) result.Add(chunk);

                if (end >= text.Length) break;

                start = Math.Max(0, end - overlap);
                start = MoveToNextWordBoundary(text, start);
            }

            return result;
        }

        private static int FindBestBreak(string text, int start, int maxEnd, int chunkSize)
        {
            var minPreferred = Math.Min(start + Math.Max(1, chunkSize / 2), maxEnd);

            for (var i = maxEnd - 1; i >= minPreferred; i--)
                if (IsSentenceBoundary(text[i])) return i + 1;

            for (var i = maxEnd - 1; i > start; i--)
                if (char.IsWhiteSpace(text[i])) return i + 1;

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

            while (index < text.Length && char.IsWhiteSpace(text[index])) index++;
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
    }
}