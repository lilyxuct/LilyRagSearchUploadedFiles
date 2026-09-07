using System.Text;
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

            var folder = _config["Ingestion:DocumentsFolder"];
            if (string.IsNullOrWhiteSpace(folder))
            {
                folder = Path.Combine(_env.ContentRootPath, "documents");
            }

            if (!Directory.Exists(folder))
            {
                _logger.LogWarning("Documents folder not found: {Folder}", folder);
                return;
            }

            var pdfFiles = Directory.EnumerateFiles(folder, "*.pdf", SearchOption.TopDirectoryOnly).ToList();
            _logger.LogInformation("Found {Count} PDF files in {Folder}.", pdfFiles.Count, folder);

            foreach (var path in pdfFiles)
            {
                if (stoppingToken.IsCancellationRequested)
                {
                    break;
                }

                var fileName = Path.GetFileName(path);

                try
                {
                    if (await _docs.IsFileIndexedAsync(fileName, stoppingToken))
                    {
                        _logger.LogInformation("Skipping already indexed file: {FileName}", fileName);
                        continue;
                    }

                    var content = (await ExtractPdfTextAsync(path, stoppingToken)).Replace("\0", string.Empty);
                    if (string.IsNullOrWhiteSpace(content))
                    {
                        _logger.LogWarning("No text extracted from {FileName}. Skipping.", fileName);
                        continue;
                    }

                    var chunks = SplitIntoChunks(content, 800, 120);
                    foreach (var chunk in chunks)
                    {
                        var embedding = await _embed.GetEmbeddingAsync(chunk);
                        await _docs.InsertDocumentAsync(fileName, chunk, embedding);
                    }

                    _logger.LogInformation("Indexed {FileName} with {ChunkCount} chunks.", fileName, chunks.Count);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed indexing file {FileName}", fileName);
                }
            }

            _logger.LogInformation("Document folder ingestion completed.");
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
            if (string.IsNullOrWhiteSpace(text))
            {
                return result;
            }

            var step = Math.Max(1, chunkSize - overlap);
            for (var i = 0; i < text.Length; i += step)
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
    }
}