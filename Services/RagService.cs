using BioTwin_AI.Data;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace BioTwin_AI.Services
{
    /// <summary>
    /// Service for managing RAG (Retrieval-Augmented Generation) operations
    /// using EF Core only.
    /// </summary>
    public class RagService : IRagService
    {
        private readonly BioTwinDbContext _dbContext;
        private readonly ILogger<RagService> _logger;
        private readonly CurrentUserSession _session;
        private const int VECTOR_SIZE = 128;

        public RagService(BioTwinDbContext dbContext, ILogger<RagService> logger, CurrentUserSession session)
        {
            _dbContext = dbContext;
            _logger = logger;
            _session = session;
        }

        private string? GetTenantIdOrNull()
        {
            return _session.IsAuthenticated ? _session.Username : null;
        }

        /// <summary>
        /// Initialize RAG infrastructure.
        /// </summary>
        public async Task InitializeAsync()
        {
            try
            {
                var count = await _dbContext.ResumeEntries.CountAsync();
                _logger.LogInformation("RAG initialized with {Count} total indexed entries", count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize RAG infrastructure");
            }
        }

        /// <summary>
        /// Create a serialized embedding payload from text content.
        /// </summary>
        public async Task<string> CreateEmbeddingPayloadAsync(string content, Dictionary<string, string> metadata)
        {
            try
            {
                _ = metadata;
                var embedding = GenerateDeterministicEmbedding(content);
                var embeddingJson = SerializeVector(embedding);

                // Keep this API to avoid changing callers. The returned value is persisted
                // by the caller into ResumeEntry.EmbeddingPayload.
                _logger.LogInformation("Generated deterministic embedding payload");
                return await Task.FromResult(embeddingJson);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create embedding payload");
                throw;
            }
        }

        /// <summary>
        /// Search for similar resume entries based on query
        /// </summary>
        public async Task<List<(string Content, double Score)>> SearchAsync(string query, int limit = 5)
        {
            try
            {
                _logger.LogInformation("Searching for query: {Query}", query);

                var tenantId = GetTenantIdOrNull();
                if (string.IsNullOrWhiteSpace(tenantId))
                {
                    return new List<(string, double)>();
                }

                var queryEmbedding = GenerateDeterministicEmbedding(query);
                var candidates = await _dbContext.ResumeEntries
                    .AsNoTracking()
                    .Where(e => e.TenantId == tenantId && e.EmbeddingPayload != null && e.EmbeddingPayload != string.Empty)
                    .Select(e => new { e.Content, e.EmbeddingPayload })
                    .ToListAsync();

                var ranked = new List<(string Content, double Score)>();

                foreach (var candidate in candidates)
                {
                    if (!TryParseVector(candidate.EmbeddingPayload!, out var vector))
                    {
                        continue;
                    }

                    var score = CosineSimilarity(queryEmbedding, vector);
                    ranked.Add((candidate.Content, score));
                }

                return ranked
                    .OrderByDescending(r => r.Score)
                    .Take(limit)
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to search embeddings");
                return new List<(string, double)>();
            }
        }

        /// <summary>
        /// Delete all embeddings for cleanup
        /// </summary>
        public async Task ClearAsync()
        {
            try
            {
                var tenantId = GetTenantIdOrNull();
                if (string.IsNullOrWhiteSpace(tenantId))
                {
                    return;
                }

                var entries = await _dbContext.ResumeEntries
                    .Where(e => e.TenantId == tenantId && e.EmbeddingPayload != null)
                    .ToListAsync();

                foreach (var entry in entries)
                {
                    entry.EmbeddingPayload = null;
                }

                await _dbContext.SaveChangesAsync();

                _logger.LogInformation("Cleared all EF-based embeddings");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to clear embeddings");
            }
        }

        private static float[] GenerateDeterministicEmbedding(string text)
        {
            var vector = new float[VECTOR_SIZE];
            var tokens = text
                .ToLowerInvariant()
                .Split(new[] { ' ', '\r', '\n', '\t', '.', ',', ';', ':', '!', '?', '(', ')', '[', ']', '{', '}', '"', '\'' },
                    StringSplitOptions.RemoveEmptyEntries);

            foreach (var token in tokens)
            {
                var hash = SHA256.HashData(Encoding.UTF8.GetBytes(token));
                var index = BitConverter.ToInt32(hash, 0) % VECTOR_SIZE;
                if (index < 0)
                {
                    index += VECTOR_SIZE;
                }

                var sign = (hash[4] & 1) == 0 ? 1f : -1f;
                var magnitude = (hash[5] / 255f) + 0.01f;
                vector[index] += sign * magnitude;
            }

            Normalize(vector);
            return vector;
        }

        private static void Normalize(float[] vector)
        {
            double sum = 0;
            for (var i = 0; i < vector.Length; i++)
            {
                sum += vector[i] * vector[i];
            }

            if (sum <= double.Epsilon)
            {
                return;
            }

            var norm = (float)Math.Sqrt(sum);
            for (var i = 0; i < vector.Length; i++)
            {
                vector[i] /= norm;
            }
        }

        private static string SerializeVector(float[] vector)
        {
            return "[" + string.Join(",", vector.Select(v => v.ToString("G9", CultureInfo.InvariantCulture))) + "]";
        }

        private static bool TryParseVector(string serialized, out float[] vector)
        {
            vector = new float[VECTOR_SIZE];

            if (string.IsNullOrWhiteSpace(serialized))
            {
                return false;
            }

            var trimmed = serialized.Trim();
            if (!trimmed.StartsWith("[") || !trimmed.EndsWith("]"))
            {
                return false;
            }

            var body = trimmed.Substring(1, trimmed.Length - 2);
            var parts = body.Split(',', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length != VECTOR_SIZE)
            {
                return false;
            }

            for (var i = 0; i < parts.Length; i++)
            {
                if (!float.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out vector[i]))
                {
                    return false;
                }
            }

            return true;
        }

        private static double CosineSimilarity(float[] left, float[] right)
        {
            if (left.Length != right.Length)
            {
                return 0;
            }

            double dot = 0;
            double leftNorm = 0;
            double rightNorm = 0;

            for (var i = 0; i < left.Length; i++)
            {
                dot += left[i] * right[i];
                leftNorm += left[i] * left[i];
                rightNorm += right[i] * right[i];
            }

            if (leftNorm <= double.Epsilon || rightNorm <= double.Epsilon)
            {
                return 0;
            }

            return dot / (Math.Sqrt(leftNorm) * Math.Sqrt(rightNorm));
        }
    }
}
