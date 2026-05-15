using Microsoft.Extensions.AI;
using System.Text;

namespace BioTwin_AI.Services
{
    /// <summary>
    /// Dedicated service for generating text embeddings via Microsoft.Extensions.AI.
    /// Separated from AgentService to break circular dependency with RagService.
    /// </summary>
    public class EmbeddingService : IEmbeddingService
    {
        private const int MaxOllamaChunkTokens = 8000;
        private const int TargetChunkTokens = 7000;
        private const int MaxChunkChars = 8000;

        private readonly ILogger<EmbeddingService> _logger;
        private readonly IEmbeddingGenerator<string, Embedding<float>> _embeddingGenerator;
        private readonly string _embeddingModel;

        public EmbeddingService(
            ILogger<EmbeddingService> logger,
            IConfiguration config,
            IEmbeddingGenerator<string, Embedding<float>> embeddingGenerator)
        {
            _logger = logger;
            _embeddingGenerator = embeddingGenerator;
            _embeddingModel = config["LLM:EmbeddingModel"] ?? "nomic-embed-text";
        }

        /// <summary>
        /// Generate embeddings for text using the configured LLM embedding model.
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
                return await EmbedChunkWithRetryAsync(chunks[0], vectorSize);
            }

            _logger.LogInformation("Embedding long markdown in {ChunkCount} chunks", chunks.Count);

            var vectors = new List<float[]>(chunks.Count);
            foreach (var chunk in chunks)
            {
                vectors.Add(await EmbedChunkWithRetryAsync(chunk, vectorSize));
            }

            return AverageVectors(vectors, vectorSize);
        }

        private async Task<float[]> EmbedChunkWithRetryAsync(string chunk, int vectorSize)
        {
            try
            {
                return await GenerateEmbeddingAsync(chunk, vectorSize);
            }
            catch (InvalidOperationException ex) when (IsContextLengthExceeded(ex) && chunk.Length > 1)
            {
                var (left, right) = SplitIntoTwoParts(chunk);
                if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right))
                {
                    throw;
                }

                _logger.LogWarning("Embedding context exceeded. Splitting chunk and retrying embedding.");

                var leftVector = await EmbedChunkWithRetryAsync(left, vectorSize);
                var rightVector = await EmbedChunkWithRetryAsync(right, vectorSize);

                return AverageVectors(new List<float[]> { leftVector, rightVector }, vectorSize);
            }
        }

        private static bool IsContextLengthExceeded(InvalidOperationException ex)
        {
            return ex.Message.Contains("input length exceeds the context length", StringComparison.OrdinalIgnoreCase)
                || ex.Message.Contains("context length", StringComparison.OrdinalIgnoreCase);
        }

        private async Task<float[]> GenerateEmbeddingAsync(string text, int vectorSize)
        {
            var options = new EmbeddingGenerationOptions
            {
                ModelId = _embeddingModel,
                Dimensions = vectorSize
            };

            var vector = await _embeddingGenerator.GenerateVectorAsync(text, options);
            return TrimOrPad(vector.ToArray(), vectorSize);
        }

        private static List<string> SplitMarkdownForEmbedding(string text)
        {
            var normalized = string.IsNullOrWhiteSpace(text)
                ? string.Empty
                : text.Replace("\r\n", "\n");

            if (normalized.Length <= MaxChunkChars && EstimateTokens(normalized) <= MaxOllamaChunkTokens)
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

                if (EstimateTokens(candidate) > MaxOllamaChunkTokens)
                {
                    takeLength = Math.Max(1, takeLength / 2);
                    candidate = remaining[..takeLength];
                }

                parts.Add(candidate);
                remaining = remaining[takeLength..];
            }

            return parts;
        }

        private static (string Left, string Right) SplitIntoTwoParts(string text)
        {
            var mid = text.Length / 2;
            var splitAt = text.LastIndexOf('\n', mid);
            if (splitAt <= 0)
            {
                splitAt = mid;
            }

            var left = text[..splitAt].Trim();
            var right = text[splitAt..].Trim();
            return (left, right);
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
