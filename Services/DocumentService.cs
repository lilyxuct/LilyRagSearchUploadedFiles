using Npgsql;

namespace LilyRagPractices.Services
{
    public class DocumentService
    {
        public sealed class SearchResult
        {
            public string Content { get; set; } = string.Empty;
            public float Score { get; set; }
        }

        private readonly string _conn;
        private const int ExpectedDimensions = 768;

        public DocumentService(IConfiguration config)
        {
            _conn = config.GetConnectionString("RagDb")
                ?? throw new ArgumentNullException("RagDb connection string is not configured.");
        }

        public async Task InsertDocumentAsync(string filename, string content, float[] embedding)
        {
            if (embedding is null || embedding.Length != ExpectedDimensions)
            {
                throw new InvalidOperationException(
                    $"Embedding must be {ExpectedDimensions} dimensions. Got {embedding?.Length ?? 0}.");
            }

            using var conn = new NpgsqlConnection(_conn);
            await conn.OpenAsync();

            using var cmd = new NpgsqlCommand(
                "INSERT INTO documents (file_name, content, embedding) VALUES (@f, @c, @e::vector)", conn);

            cmd.Parameters.AddWithValue("f", filename);
            cmd.Parameters.AddWithValue("c", content);
            cmd.Parameters.AddWithValue("e", embedding);

            await cmd.ExecuteNonQueryAsync();
        }

        public async Task<bool> IsFileIndexedAsync(string filename, CancellationToken cancellationToken = default)
        {
            using var conn = new NpgsqlConnection(_conn);
            await conn.OpenAsync(cancellationToken);

            using var cmd = new NpgsqlCommand(
                "SELECT EXISTS (SELECT 1 FROM documents WHERE file_name = @f)", conn);

            cmd.Parameters.AddWithValue("f", filename);

            var result = await cmd.ExecuteScalarAsync(cancellationToken);
            return result is bool b && b;
        }

        public async Task<List<SearchResult>> SearchAsync(float[] queryEmbedding, int limit = 5)
        {
            if (queryEmbedding is null || queryEmbedding.Length != ExpectedDimensions)
            {
                throw new InvalidOperationException(
                    $"Query embedding must be {ExpectedDimensions} dimensions. Got {queryEmbedding?.Length ?? 0}.");
            }

            using var conn = new NpgsqlConnection(_conn);
            await conn.OpenAsync();

            using var cmd = new NpgsqlCommand(
                @"SELECT content, 1 - (embedding <=> @q::vector) AS score
                  FROM documents
                  ORDER BY embedding <=> @q::vector
                  LIMIT @limit", conn);

            cmd.Parameters.AddWithValue("q", queryEmbedding);
            cmd.Parameters.AddWithValue("limit", limit);

            var results = new List<SearchResult>();
            using var reader = await cmd.ExecuteReaderAsync();

            while (await reader.ReadAsync())
            {
                results.Add(new SearchResult
                {
                    Content = reader.GetString(0),
                    Score = reader.GetFloat(1)
                });
            }

            return results;
        }
    }
}