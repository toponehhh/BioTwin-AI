using BioTwin_AI.Data;
using BioTwin_AI.Models;
using Microsoft.EntityFrameworkCore;
using System.Globalization;

namespace BioTwin_AI.Services
{
    /// <summary>
    /// Service for managing RAG (Retrieval-Augmented Generation) operations
    /// using LLM-based embeddings from AgentService.
    /// </summary>
    public class RagService : IRagService
    {
        private readonly BioTwinDbContext _dbContext;
        private readonly ILogger<RagService> _logger;
        private readonly CurrentUserSession _session;
        private readonly IEmbeddingService _embeddingService;
        private readonly int _vectorSize;

        public RagService(BioTwinDbContext dbContext, ILogger<RagService> logger, CurrentUserSession session, IEmbeddingService embeddingService, IConfiguration config)
        {
            _dbContext = dbContext;
            _logger = logger;
            _session = session;
            _embeddingService = embeddingService;
            _vectorSize = config.GetValue("Rag:EmbeddingSize", 768);
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
                var count = await _dbContext.ResumeSections.CountAsync();
                _logger.LogInformation("RAG initialized with {Count} total indexed resume sections", count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize RAG infrastructure");
            }
        }

        /// <summary>
        /// Create a serialized embedding payload from text content using LLM.
        /// </summary>
        public async Task<string> CreateEmbeddingPayloadAsync(string content, Dictionary<string, string> metadata)
        {
            try
            {
                _ = metadata;
                var embedding = await _embeddingService.GetEmbeddingAsync(content, _vectorSize);
                var embeddingJson = SerializeVector(embedding);

                // The returned value is persisted by the caller into ResumeSection.EmbeddingPayload.
                _logger.LogInformation("Generated LLM-based embedding payload");
                return embeddingJson;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create embedding payload");
                throw;
            }
        }

        /// <summary>
        /// Search for similar resume entries based on query using LLM embeddings.
        /// Interviewers can search all resumes; candidates can only search their own.
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

                var queryEmbedding = await _embeddingService.GetEmbeddingAsync(query, _vectorSize);

                // Interviewers search all resumes; candidates search only their own
                IQueryable<ResumeSection> query_base;
                if (_session.IsInterviewer)
                {
                    // Interviewer: search all resumes from all tenants
                    query_base = _dbContext.ResumeSections
                        .AsNoTracking()
                        .Where(e => e.EmbeddingPayload != null && e.EmbeddingPayload != string.Empty);
                }
                else
                {
                    // Candidate: search only their own resumes
                    query_base = _dbContext.ResumeSections
                        .AsNoTracking()
                        .Where(e => e.TenantId == tenantId && e.EmbeddingPayload != null && e.EmbeddingPayload != string.Empty);
                }

                var candidates = await query_base
                    .Select(e => new { e.Content, e.EmbeddingPayload, e.TenantId, e.Title })
                    .ToListAsync();

                var ranked = new List<(string Content, double Score)>();

                foreach (var candidate in candidates)
                {
                    if (!TryParseVector(candidate.EmbeddingPayload!, out var vector))
                    {
                        continue;
                    }

                    var score = CosineSimilarity(queryEmbedding, vector);
                    // Include tenant info for interviewer context
                    var contentWithContext = _session.IsInterviewer
                        ? $"[{candidate.TenantId} - {candidate.Title}]\n{candidate.Content}"
                        : candidate.Content;
                    ranked.Add((contentWithContext, score));
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

                var entries = await _dbContext.ResumeSections
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

        // LLM-based embeddings are now generated by AgentService.GetEmbeddingAsync()
        // Previously used deterministic SHA256 hashing, now using actual LLM embedding models.

        private static string SerializeVector(float[] vector)
        {
            return "[" + string.Join(",", vector.Select(v => v.ToString("G9", CultureInfo.InvariantCulture))) + "]";
        }

        private bool TryParseVector(string serialized, out float[] vector)
        {
            vector = new float[_vectorSize];

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
            if (parts.Length != _vectorSize)
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
