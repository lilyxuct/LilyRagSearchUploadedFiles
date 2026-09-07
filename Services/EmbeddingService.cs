using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System.Text;
using System.Linq;
using System.Collections.Generic;

namespace LilyRagPractices.Services
{
    public class EmbeddingService
    {
        private readonly HttpClient _http = new();
        private readonly string _embeddingModel;
        private readonly int _expectedDimensions;
        private const int MaxChunkLength = 2000;

        public EmbeddingService(IConfiguration config)
        {
            _embeddingModel = config["Embedding:Model"] ?? "nomic-embed-text";
            _expectedDimensions = config.GetValue<int?>("Embedding:ExpectedDimensions") ?? 768;
        }

        public async Task<float[]> GetEmbeddingAsync(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                throw new ArgumentException("Input text cannot be empty.", nameof(text));

            var chunks = ChunkText(text, MaxChunkLength);
            var vectors = new List<float[]>();

            foreach (var chunk in chunks)
            {
                vectors.Add(await GetSingleEmbeddingAsync(chunk));
            }

            return AverageVectors(vectors);
        }

        private async Task<float[]> GetSingleEmbeddingAsync(string text)
        {
            var payload = new
            {
                model = _embeddingModel,
                prompt = text
            };

            var response = await _http.PostAsync(
                "http://localhost:11434/api/embeddings",
                new StringContent(JsonConvert.SerializeObject(payload),
                Encoding.UTF8, "application/json"));

            var json = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException(
                    $"Embedding request failed with status {(int)response.StatusCode} ({response.StatusCode}). Body: {json}");
            }

            var obj = JsonConvert.DeserializeObject<JObject>(json);
            var embeddingToken = obj?["embedding"];

            if (embeddingToken is null || embeddingToken.Type != JTokenType.Array)
            {
                throw new InvalidOperationException(
                    $"Embedding response does not contain a valid 'embedding' array. Body: {json}");
            }

            var vector = embeddingToken
                .Values<double>()
                .Select(x => (float)x)
                .ToArray();

            if (vector.Length != _expectedDimensions)
            {
                throw new InvalidOperationException(
                    $"Embedding model '{_embeddingModel}' returned {vector.Length} dimensions. Expected {_expectedDimensions}.");
            }

            return vector;
        }

        private static List<string> ChunkText(string text, int maxChunkLength)
        {
            var chunks = new List<string>();
            var index = 0;

            while (index < text.Length)
            {
                var length = Math.Min(maxChunkLength, text.Length - index);
                chunks.Add(text.Substring(index, length));
                index += length;
            }

            return chunks;
        }

        private static float[] AverageVectors(List<float[]> vectors)
        {
            if (vectors.Count == 0)
                throw new InvalidOperationException("No embeddings were generated.");

            var size = vectors[0].Length;
            var result = new float[size];

            foreach (var vector in vectors)
            {
                if (vector.Length != size)
                    throw new InvalidOperationException("Embedding size mismatch across chunks.");

                for (var i = 0; i < size; i++)
                {
                    result[i] += vector[i];
                }
            }

            for (var i = 0; i < size; i++)
            {
                result[i] /= vectors.Count;
            }

            return result;
        }
    }
}
