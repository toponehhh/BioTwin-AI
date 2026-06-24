using System.Text;

namespace BioTwin_AI.Services
{
    /// <summary>
    /// Dedicated service for generating text embeddings with the local BGE-M3 ONNX model.
    /// Separated from AgentService to break circular dependency with RagService.
    /// </summary>
    public class EmbeddingService : IEmbeddingService
    {
        private const int MaxEmbeddingChunkTokens = 8000;
        private const int TargetChunkTokens = 7000;
        private const int MaxChunkChars = 8000;

        private readonly ILogger<EmbeddingService> _logger;
        private readonly ILocalEmbeddingModel _embeddingModel;

        public EmbeddingService(
            ILogger<EmbeddingService> logger,
            ILocalEmbeddingModel embeddingModel)
        {
            _logger = logger;
            _embeddingModel = embeddingModel;
        }

        /// <summary>
        /// Generate embeddings for text using the local BGE-M3 embedding model.
        /// </summary>
        public async Task<float[]> GetEmbeddingAsync(string text, int vectorSize = 768)
        {
            try
            {
                return await GenerateEmbeddingWithChunkingAsync(text, vectorSize);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate embedding");
                throw;
            }
        }

        private async Task<float[]> GenerateEmbeddingWithChunkingAsync(string text, int vectorSize)
        {
            var chunks = SplitMarkdownForEmbedding(text);

            if (chunks.Count == 1)
            {
                return await GenerateEmbeddingAsync(chunks[0], vectorSize);
            }

            _logger.LogInformation("Embedding long markdown in {ChunkCount} chunks", chunks.Count);

            var vectors = new List<float[]>(chunks.Count);
            foreach (var chunk in chunks)
            {
                vectors.Add(await GenerateEmbeddingAsync(chunk, vectorSize));
            }

            return AverageVectors(vectors, vectorSize);
        }

        private Task<float[]> GenerateEmbeddingAsync(string text, int vectorSize)
        {
            var vector = _embeddingModel.GenerateEmbedding(text);
            return Task.FromResult(TrimOrPad(vector, vectorSize));
        }

        private static List<string> SplitMarkdownForEmbedding(string text)
        {
            var normalized = string.IsNullOrWhiteSpace(text)
                ? string.Empty
                : text.Replace("\r\n", "\n");

            if (normalized.Length <= MaxChunkChars && EstimateTokens(normalized) <= MaxEmbeddingChunkTokens)
            {
                return new List<string> { normalized };
            }

            var chunks = new List<string>();
            var paragraphs = normalized.Split("\n\n", StringSplitOptions.None);
            var current = new StringBuilder();

            foreach (var paragraph in paragraphs)
            {
                if (string.IsNullOrWhiteSpace(paragraph))
                {
                    continue;
                }

                var candidate = current.Length == 0
                    ? paragraph
                    : $"{current}\n\n{paragraph}";

                var candidateTokens = EstimateTokens(candidate);
                if (candidateTokens <= TargetChunkTokens && candidate.Length <= MaxChunkChars)
                {
                    if (current.Length == 0)
                    {
                        current.Append(paragraph);
                    }
                    else
                    {
                        current.Append("\n\n");
                        current.Append(paragraph);
                    }
                    continue;
                }

                if (current.Length > 0)
                {
                    chunks.Add(current.ToString());
                    current.Clear();
                }

                if (EstimateTokens(paragraph) <= TargetChunkTokens && paragraph.Length <= MaxChunkChars)
                {
                    current.Append(paragraph);
                }
                else
                {
                    chunks.AddRange(SplitOversizedText(paragraph));
                }
            }

            if (current.Length > 0)
            {
                chunks.Add(current.ToString());
            }

            return chunks.Count == 0 ? new List<string> { string.Empty } : chunks;
        }

        private static List<string> SplitOversizedText(string text)
        {
            var parts = new List<string>();
            var remaining = text;

            while (!string.IsNullOrWhiteSpace(remaining))
            {
                var takeLength = Math.Min(MaxChunkChars, remaining.Length);
                var candidate = remaining[..takeLength];

                if (EstimateTokens(candidate) > MaxEmbeddingChunkTokens)
                {
                    takeLength = Math.Max(1, takeLength / 2);
                    candidate = remaining[..takeLength];
                }

                parts.Add(candidate);
                remaining = remaining[takeLength..];
            }

            return parts;
        }

        private static int EstimateTokens(string text)
        {
            if (string.IsNullOrEmpty(text))
            {
                return 0;
            }

            var tokens = 0;
            var inAsciiWord = false;

            foreach (var ch in text)
            {
                if (ch <= 0x7F)
                {
                    if (char.IsLetterOrDigit(ch))
                    {
                        if (!inAsciiWord)
                        {
                            tokens++;
                            inAsciiWord = true;
                        }
                    }
                    else if (char.IsWhiteSpace(ch))
                    {
                        inAsciiWord = false;
                    }
                    else
                    {
                        tokens++;
                        inAsciiWord = false;
                    }
                }
                else
                {
                    tokens++;
                    inAsciiWord = false;
                }
            }

            return tokens;
        }

        private static float[] AverageVectors(IReadOnlyList<float[]> vectors, int vectorSize)
        {
            if (vectors.Count == 0)
            {
                return new float[vectorSize];
            }

            var sum = new float[vectorSize];
            foreach (var vector in vectors)
            {
                for (var i = 0; i < vectorSize && i < vector.Length; i++)
                {
                    sum[i] += vector[i];
                }
            }

            for (var i = 0; i < vectorSize; i++)
            {
                sum[i] /= vectors.Count;
            }

            return sum;
        }

        private static float[] TrimOrPad(float[] embedding, int vectorSize)
        {
            var result = embedding.Take(vectorSize).ToArray();
            if (result.Length < vectorSize)
            {
                Array.Resize(ref result, vectorSize);
            }
            return result;
        }
    }
}
