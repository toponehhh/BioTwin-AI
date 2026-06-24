using BioTwin_AI.Data;
using BioTwin_AI.Models;
using Microsoft.Extensions.AI;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BioTwin_AI.Services
{
    /// <summary>
    /// Service for managing RAG (Retrieval-Augmented Generation) operations
    /// using locally generated embeddings.
    /// </summary>
    public class RagService : IRagService
    {
        private static readonly HashSet<string> QueryStopWords = new(StringComparer.OrdinalIgnoreCase)
        {
            "A", "AN", "THE", "AND", "OR", "TO", "OF", "IN", "ON", "AT", "BY", "FOR",
            "WITH", "FROM", "ABOUT", "WHICH", "WHAT", "WHO", "WHEN", "WHERE", "WHY",
            "HOW", "ME", "MY", "YOUR", "YOU", "I", "WE", "OUR", "HAVE", "HAS", "HAD",
            "DO", "DID", "DOES", "BE", "AM", "IS", "ARE", "WAS", "WERE", "BEEN",
            "BEFORE", "ANY", "SOME", "PLEASE", "SHOW", "TELL"
        };

        private static readonly HashSet<string> QueryIntentTerms = new(StringComparer.OrdinalIgnoreCase)
        {
            "PROJECT", "PROJECTS", "EXPERIENCE", "EXPERIENCES", "WORK", "WORKED",
            "WORKING", "JOB", "JOBS", "ROLE", "ROLES"
        };

        private static readonly string[] EntityLabels =
        [
            "Company", "Campany", "Employer", "Organization", "Client", "Customer",
            "Project Name", "Project", "Product", "Role", "Title", "Institution",
            "School", "University", "Degree", "Major", "Technology", "Tech Stack", "Skill", "Skills"
        ];

        private readonly BioTwinDbContext _dbContext;
        private readonly ILogger<RagService> _logger;
        private readonly CurrentUserSession _session;
        private readonly IEmbeddingService _embeddingService;
        private readonly IChatClient _chatClient;
        private readonly int _vectorSize;
        private readonly bool _rerankEnabled;
        private readonly int _rerankCandidateLimit;
        private readonly int _rerankSnippetChars;
        private readonly int _rerankMaxTokens;
        private readonly bool _isOllamaProvider;
        private readonly string _chatModel;
        private readonly int _chatNumCtx;

        public RagService(
            BioTwinDbContext dbContext,
            ILogger<RagService> logger,
            CurrentUserSession session,
            IEmbeddingService embeddingService,
            IChatClient chatClient,
            IConfiguration config)
        {
            _dbContext = dbContext;
            _logger = logger;
            _session = session;
            _embeddingService = embeddingService;
            _chatClient = chatClient;
            _vectorSize = config.GetValue("Rag:EmbeddingSize", 768);
            _rerankEnabled = config.GetValue("Rag:EnableRerank", true);
            _rerankCandidateLimit = Math.Max(2, config.GetValue("Rag:RerankCandidateLimit", 8));
            _rerankSnippetChars = Math.Max(200, config.GetValue("Rag:RerankSnippetChars", 1200));
            _rerankMaxTokens = Math.Max(32, config.GetValue("Rag:RerankMaxTokens", 128));
            _isOllamaProvider = string.Equals(config["LLM:Provider"] ?? "Ollama", "Ollama", StringComparison.OrdinalIgnoreCase);
            _chatModel = config["LLM:Model"] ?? "gemma4:e2b";
            _chatNumCtx = config.GetValue("LLM:ChatNumCtx", 2048);
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
                await EnsureVectorStoreSchemaAsync();

                var count = await _dbContext.ResumeSectionVectors.CountAsync();
                _logger.LogInformation("RAG initialized with {Count} total indexed resume section vectors", count);
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
                var embeddingInput = BuildEmbeddingInput(content, metadata);
                if (string.IsNullOrWhiteSpace(embeddingInput))
                {
                    _logger.LogWarning("Skipping embedding generation for an empty resume section.");
                    return SerializeVector(new float[_vectorSize]);
                }

                var embedding = await _embeddingService.GetEmbeddingAsync(embeddingInput, _vectorSize);
                var embeddingJson = SerializeVector(embedding);

                // The returned value is persisted by the caller into ResumeSectionVector.EmbeddingPayload.
                _logger.LogInformation("Generated local embedding payload");
                return embeddingJson;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create embedding payload");
                throw;
            }
        }

        public async Task<string> CreateEmbeddingPayloadAsync(ResumeSectionChunk chunk)
        {
            try
            {
                var embeddingInput = chunk.ToEmbeddingText();
                if (string.IsNullOrWhiteSpace(embeddingInput))
                {
                    _logger.LogWarning("Skipping embedding generation for an empty resume section chunk.");
                    return SerializeVector(new float[_vectorSize]);
                }

                var embedding = await _embeddingService.GetEmbeddingAsync(embeddingInput, _vectorSize);
                _logger.LogInformation("Generated local embedding payload from section chunk");
                return SerializeVector(embedding);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to create embedding payload from chunk");
                throw;
            }
        }

        private static string BuildEmbeddingInput(string content, Dictionary<string, string> metadata)
        {
            metadata.TryGetValue("title", out var title);
            metadata.TryGetValue("parent_section_title", out var parentSectionTitle);

            var parts = new List<string>();

            if (!string.IsNullOrWhiteSpace(title))
            {
                parts.Add($"Section Title: {title}");
            }

            if (!string.IsNullOrWhiteSpace(parentSectionTitle))
            {
                parts.Add($"Parent Section Title: {parentSectionTitle}");
            }

            if (!string.IsNullOrWhiteSpace(content))
            {
                parts.Add($"Section Content:\n{content}");
            }

            return parts.Count == 0 ? string.Empty : string.Join("\n\n", parts);
        }

        /// <summary>
        /// Search for similar resume entries based on query embeddings.
        /// Interviewers can search all resumes; candidates can only search their own.
        /// </summary>
        public async Task<List<(string Content, double Score)>> SearchAsync(string query, int limit = 5)
        {
            return await SearchCoreAsync(query, limit, candidateLimit: limit, rerank: false);
        }

        /// <summary>
        /// Search for chat context using vector retrieval followed by optional LLM reranking.
        /// </summary>
        public async Task<List<(string Content, double Score)>> SearchForChatAsync(string query, int limit = 5)
        {
            var candidateLimit = Math.Max(limit, _rerankCandidateLimit);
            return await SearchCoreAsync(query, limit, candidateLimit, _rerankEnabled);
        }

        private async Task<List<(string Content, double Score)>> SearchCoreAsync(
            string query,
            int limit,
            int candidateLimit,
            bool rerank)
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
                var candidates = await LoadVectorCandidatesAsync(tenantId);
                var ranked = RankCandidates(query, queryEmbedding, candidates);

                if (ranked.Count == 0)
                {
                    return new List<(string, double)>();
                }

                var vectorResults = ranked
                    .Take(Math.Max(limit, candidateLimit))
                    .Select(candidate => (candidate.Content, candidate.DisplayScore))
                    .ToList();

                if (!rerank || vectorResults.Count <= 1)
                {
                    return vectorResults
                        .Take(limit)
                        .ToList();
                }

                return await RerankCandidatesAsync(query, ranked.Take(candidateLimit).ToList(), limit);
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

                var entries = await _dbContext.ResumeSectionVectors
                    .Where(e => e.TenantId == tenantId)
                    .ToListAsync();

                _dbContext.ResumeSectionVectors.RemoveRange(entries);
                await _dbContext.SaveChangesAsync();

                if (_dbContext.Database.IsRelational() && await LegacyVectorColumnExistsAsync())
                {
                    await _dbContext.Database.ExecuteSqlInterpolatedAsync(
                        $"UPDATE ResumeSections SET VectorId = NULL WHERE TenantId = {tenantId};");
                }

                _logger.LogInformation("Cleared all EF-based embeddings");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to clear embeddings");
            }
        }

        // Embeddings are generated by IEmbeddingService through the local BGE-M3 model.

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

        private async Task<List<ResumeSectionVector>> LoadVectorCandidatesAsync(string tenantId)
        {
            IQueryable<ResumeSectionVector> queryBase;
            if (_session.IsInterviewer)
            {
                queryBase = _dbContext.ResumeSectionVectors
                    .AsNoTracking()
                    .Where(e => e.EmbeddingPayload != null && e.EmbeddingPayload != string.Empty);
            }
            else
            {
                queryBase = _dbContext.ResumeSectionVectors
                    .AsNoTracking()
                    .Where(e => e.TenantId == tenantId && e.EmbeddingPayload != null && e.EmbeddingPayload != string.Empty);
            }

            return await queryBase.ToListAsync();
        }

        private List<SearchCandidate> RankCandidates(string query, float[] queryEmbedding, IEnumerable<ResumeSectionVector> candidates)
        {
            var ranked = new List<SearchCandidate>();
            var candidateList = candidates.ToList();
            var profile = BuildLexicalProfile(candidateList);
            var lexicalTerms = ExtractLexicalTerms(query, profile);

            foreach (var candidate in candidateList)
            {
                if (!TryParseVector(candidate.EmbeddingPayload, out var vector))
                {
                    continue;
                }

                var score = CosineSimilarity(queryEmbedding, vector);
                var chunk = ResumeSectionChunk.Parse(candidate.Content);

                // Build the section path: prefer breadcrumb from chunk metadata (new format),
                // fall back to the denormalised DB columns (old plain-text records).
                string sectionPath;
                if (chunk.Metadata.TitleBreadcrumb.Count > 0)
                {
                    sectionPath = string.Join(" > ", chunk.Metadata.TitleBreadcrumb);
                }
                else
                {
                    var title = !string.IsNullOrWhiteSpace(chunk.Metadata.SectionTitle)
                        ? chunk.Metadata.SectionTitle
                        : (string.IsNullOrWhiteSpace(candidate.SectionTitle) ? "Untitled section" : candidate.SectionTitle);
                    sectionPath = string.IsNullOrWhiteSpace(candidate.ParentSectionTitle)
                        ? title
                        : $"{candidate.ParentSectionTitle} > {title}";
                }

                // TenantId: prefer the DB column (always populated) over chunk metadata.
                var tenantId = !string.IsNullOrWhiteSpace(candidate.TenantId)
                    ? candidate.TenantId
                    : chunk.Metadata.TenantId;

                var prefix = _session.IsInterviewer
                    ? $"[{tenantId} - {sectionPath}]"
                    : $"[{sectionPath}]";

                var chunkText = !string.IsNullOrWhiteSpace(chunk.Chunk)
                    ? chunk.Chunk
                    : candidate.Content;   // plain-text fallback

                var contentWithContext = $"{prefix}\n{chunkText}";
                var lexicalScore = CalculateLexicalScore(lexicalTerms, contentWithContext);
                ranked.Add(new SearchCandidate(contentWithContext, score, lexicalScore.EntityScore, lexicalScore.IntentScore));
            }

            return ranked
                .OrderByDescending(candidate => candidate.RetrievalScore)
                .ToList();
        }

        private static ResumeLexicalProfile BuildLexicalProfile(IEnumerable<ResumeSectionVector> candidates)
        {
            var entities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var candidate in candidates)
            {
                var chunk = ResumeSectionChunk.Parse(candidate.Content);
                AddEntityValue(entities, candidate.SectionTitle);
                AddEntityValue(entities, candidate.ParentSectionTitle);
                AddEntityValue(entities, chunk.Metadata.SectionTitle);

                foreach (var title in chunk.Metadata.TitleBreadcrumb)
                {
                    AddEntityValue(entities, title);
                }

                ExtractStructuredEntities(candidate.Content, entities);
                ExtractStructuredEntities(chunk.Chunk, entities);
            }

            return new ResumeLexicalProfile(entities);
        }

        private static void ExtractStructuredEntities(string? text, ISet<string> entities)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return;
            }

            foreach (var line in text.Replace("\\n", "\n").Split('\n'))
            {
                foreach (var label in EntityLabels)
                {
                    var pattern = $@"^\s*(?:[-*]\s*)?(?:#+\s*)?{Regex.Escape(label)}\s*:?\s*\**\s*(.+?)\s*\**\s*$";
                    var match = Regex.Match(line, pattern, RegexOptions.IgnoreCase);
                    if (match.Success)
                    {
                        AddEntityValue(entities, match.Groups[1].Value);
                    }
                }
            }
        }

        private static void AddEntityValue(ISet<string> entities, string? value)
        {
            var cleaned = CleanEntityValue(value);
            if (string.IsNullOrWhiteSpace(cleaned))
            {
                return;
            }

            AddEntityAlias(entities, cleaned);

            foreach (Match match in Regex.Matches(cleaned, @"[\p{L}\p{N}][\p{L}\p{N}._+#-]{1,}"))
            {
                var token = match.Value.Trim();
                if (!QueryStopWords.Contains(token) && !QueryIntentTerms.Contains(token))
                {
                    AddEntityAlias(entities, token);
                }
            }
        }

        private static void AddEntityAlias(ISet<string> entities, string value)
        {
            var normalized = NormalizeEntityKey(value);
            if (normalized.Length >= 2)
            {
                entities.Add(normalized);
            }
        }

        private static string CleanEntityValue(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var cleaned = Regex.Replace(value, @"[*_`#>\[\]()]+", " ");
            cleaned = Regex.Replace(cleaned, @"\s+", " ");
            return cleaned.Trim(' ', ':', '-', '|');
        }

        private static string NormalizeEntityKey(string value)
        {
            var chars = value
                .Where(char.IsLetterOrDigit)
                .Select(char.ToUpperInvariant)
                .ToArray();

            return new string(chars);
        }

        private static LexicalQueryTerms ExtractLexicalTerms(string query, ResumeLexicalProfile profile)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return new LexicalQueryTerms(Array.Empty<string>(), Array.Empty<string>());
            }

            var entityTerms = new List<string>();
            var intentTerms = new List<string>();
            var tokens = Regex.Matches(query, @"[\p{L}\p{N}][\p{L}\p{N}._+#-]{1,}")
                .Select(match => match.Value.Trim())
                .Where(term => term.Length >= 2 && !IsMostlyCjk(term))
                .Distinct(StringComparer.OrdinalIgnoreCase);

            foreach (var rawToken in tokens)
            {
                var token = rawToken.ToUpperInvariant();
                if (QueryStopWords.Contains(token))
                {
                    continue;
                }

                if (QueryIntentTerms.Contains(token))
                {
                    intentTerms.Add(NormalizeIntentTerm(token));
                    continue;
                }

                if (profile.ContainsEntity(rawToken) || LooksLikeEntityToken(rawToken))
                {
                    entityTerms.Add(rawToken);
                }
            }

            if (query.Contains("项目", StringComparison.Ordinal))
            {
                intentTerms.Add("PROJECT");
            }

            if (query.Contains("经历", StringComparison.Ordinal) ||
                query.Contains("工作", StringComparison.Ordinal))
            {
                intentTerms.Add("EXPERIENCE");
                intentTerms.Add("WORK");
            }

            return new LexicalQueryTerms(
                entityTerms.Distinct(StringComparer.OrdinalIgnoreCase).ToList(),
                intentTerms.Distinct(StringComparer.OrdinalIgnoreCase).ToList());
        }

        private static string NormalizeIntentTerm(string term)
        {
            return term.ToUpperInvariant() switch
            {
                "PROJECTS" => "PROJECT",
                "EXPERIENCES" => "EXPERIENCE",
                "WORKED" or "WORKING" => "WORK",
                "JOBS" => "JOB",
                "ROLES" => "ROLE",
                _ => term.ToUpperInvariant()
            };
        }

        private static bool IsMostlyCjk(string value)
        {
            var lettersOrDigits = value.Count(char.IsLetterOrDigit);
            if (lettersOrDigits == 0)
            {
                return false;
            }

            var cjk = value.Count(ch => ch >= '\u4e00' && ch <= '\u9fff');
            return cjk > 0 && cjk >= lettersOrDigits / 2d;
        }

        private static bool LooksLikeEntityToken(string token)
        {
            return token.Any(char.IsDigit) ||
                   token.Any(ch => ch is '.' or '#' or '+') ||
                   token.Count(char.IsUpper) >= 2;
        }

        private static LexicalScore CalculateLexicalScore(LexicalQueryTerms terms, string content)
        {
            if ((terms.EntityTerms.Count == 0 && terms.IntentTerms.Count == 0) ||
                string.IsNullOrWhiteSpace(content))
            {
                return new LexicalScore(0, 0);
            }

            var entityScore = terms.EntityTerms.Sum(term => HasEntityTerm(content, term) ? 1.1d : 0d);
            var intentScore = terms.IntentTerms.Sum(term => HasWholeTerm(content, term) ? 0.08d : 0d);

            return new LexicalScore(Math.Min(entityScore, 2.2d), Math.Min(intentScore, 0.24d));
        }

        private static bool HasWholeTerm(string content, string term)
        {
            return Regex.IsMatch(
                content,
                $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(term)}(?![\p{{L}}\p{{N}}])",
                RegexOptions.IgnoreCase);
        }

        private static bool HasEntityTerm(string content, string term)
        {
            return HasWholeTerm(content, term) ||
                   NormalizeEntityKey(content).Contains(NormalizeEntityKey(term), StringComparison.OrdinalIgnoreCase);
        }

        private async Task<List<(string Content, double Score)>> RerankCandidatesAsync(
            string query,
            IReadOnlyList<SearchCandidate> candidates,
            int limit)
        {
            try
            {
                var response = await _chatClient.GetResponseAsync(
                    BuildRerankMessages(query, candidates),
                    CreateRerankChatOptions());

                var rerankedIndexes = ParseRerankIndexes(response.Text, candidates.Count);
                if (rerankedIndexes.Count == 0)
                {
                    _logger.LogWarning("Rerank model returned no usable ordering. Falling back to vector ranking.");
                    return candidates
                        .Take(limit)
                        .Select(candidate => (candidate.Content, candidate.DisplayScore))
                        .ToList();
                }

                var reranked = new List<(string Content, double Score)>();
                var used = new HashSet<int>();

                foreach (var index in rerankedIndexes)
                {
                    if (!used.Add(index))
                    {
                        continue;
                    }

                    var candidate = candidates[index];
                    var score = 1d - (reranked.Count / (double)Math.Max(limit, candidates.Count));
                    reranked.Add((candidate.Content, score));

                    if (reranked.Count == limit)
                    {
                        return reranked;
                    }
                }

                for (var i = 0; i < candidates.Count; i++)
                {
                    if (reranked.Count == limit)
                    {
                        break;
                    }

                    if (!used.Add(i))
                    {
                        continue;
                    }

                    var candidate = candidates[i];
                    reranked.Add((candidate.Content, candidate.DisplayScore));
                }

                return reranked;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Rerank failed. Falling back to vector ranking.");
                return candidates
                    .Take(limit)
                    .Select(candidate => (candidate.Content, candidate.DisplayScore))
                    .ToList();
            }
        }

        private ChatMessage[] BuildRerankMessages(string query, IReadOnlyList<SearchCandidate> candidates)
        {
            var systemPrompt = """
You rank resume snippets for relevance to a user question.
Return JSON only.
Use one of these formats:
{"rankedIndexes":[0,1,2]}
or
[0,1,2]
Order indexes from most relevant to least relevant.
Prefer snippets that directly answer the question with concrete factual evidence.
Do not include explanations.
""";

            var userPrompt = new System.Text.StringBuilder();
            userPrompt.AppendLine("Question:");
            userPrompt.AppendLine(query);
            userPrompt.AppendLine();
            userPrompt.AppendLine("Candidates:");

            for (var i = 0; i < candidates.Count; i++)
            {
                var snippet = candidates[i].Content;
                if (snippet.Length > _rerankSnippetChars)
                {
                    snippet = snippet[.._rerankSnippetChars] + "\n...[truncated]";
                }

                userPrompt.AppendLine($"Index: {i}");
                userPrompt.AppendLine(snippet);
                userPrompt.AppendLine();
            }

            return
            [
                new ChatMessage(ChatRole.System, systemPrompt),
                new ChatMessage(ChatRole.User, userPrompt.ToString())
            ];
        }

        private ChatOptions CreateRerankChatOptions()
        {
            var options = new ChatOptions
            {
                ModelId = _chatModel,
                Temperature = 0,
                MaxOutputTokens = _rerankMaxTokens
            };

            return options;
        }

        private static List<int> ParseRerankIndexes(string? responseText, int candidateCount)
        {
            if (string.IsNullOrWhiteSpace(responseText))
            {
                return new List<int>();
            }

            var cleaned = responseText.Trim();
            cleaned = Regex.Replace(cleaned, @"^```(?:json)?\s*", string.Empty, RegexOptions.IgnoreCase);
            cleaned = Regex.Replace(cleaned, @"\s*```$", string.Empty);

            return TryParseJsonIndexes(cleaned, candidateCount, out var parsed)
                ? parsed
                : new List<int>();
        }

        private static bool TryParseJsonIndexes(string cleaned, int candidateCount, out List<int> indexes)
        {
            indexes = new List<int>();

            try
            {
                using var document = JsonDocument.Parse(cleaned);
                JsonElement arrayElement;

                if (document.RootElement.ValueKind == JsonValueKind.Array)
                {
                    arrayElement = document.RootElement;
                }
                else if (document.RootElement.ValueKind == JsonValueKind.Object &&
                         document.RootElement.TryGetProperty("rankedIndexes", out var rankedIndexes))
                {
                    arrayElement = rankedIndexes;
                }
                else
                {
                    return false;
                }

                if (arrayElement.ValueKind != JsonValueKind.Array)
                {
                    return false;
                }

                indexes = arrayElement.EnumerateArray()
                    .Where(element => element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out _))
                    .Select(element => element.GetInt32())
                    .Where(index => index >= 0 && index < candidateCount)
                    .Distinct()
                    .ToList();

                return indexes.Count > 0;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        private async Task EnsureVectorStoreSchemaAsync()
        {
            if (!_dbContext.Database.IsRelational())
            {
                return;
            }

            await _dbContext.Database.ExecuteSqlRawAsync("""
                CREATE TABLE IF NOT EXISTS ResumeSectionVectors (
                    Id INTEGER NOT NULL CONSTRAINT PK_ResumeSectionVectors PRIMARY KEY AUTOINCREMENT,
                    ResumeSectionId INTEGER NOT NULL,
                    TenantId TEXT NOT NULL,
                    SectionTitle TEXT NOT NULL,
                    ParentSectionTitle TEXT NULL,
                    Content TEXT NOT NULL,
                    EmbeddingPayload TEXT NOT NULL,
                    CreatedAt TEXT NOT NULL,
                    CONSTRAINT FK_ResumeSectionVectors_ResumeSections_ResumeSectionId
                        FOREIGN KEY (ResumeSectionId) REFERENCES ResumeSections (Id) ON DELETE CASCADE
                );
                """);

            await _dbContext.Database.ExecuteSqlRawAsync("""
                CREATE UNIQUE INDEX IF NOT EXISTS IX_ResumeSectionVectors_ResumeSectionId
                ON ResumeSectionVectors (ResumeSectionId);
                """);

            await _dbContext.Database.ExecuteSqlRawAsync("""
                CREATE INDEX IF NOT EXISTS IX_ResumeSectionVectors_TenantId
                ON ResumeSectionVectors (TenantId);
                """);

            await _dbContext.Database.ExecuteSqlRawAsync("""
                CREATE INDEX IF NOT EXISTS IX_ResumeSectionVectors_TenantId_CreatedAt
                ON ResumeSectionVectors (TenantId, CreatedAt);
                """);

            if (!await LegacyVectorColumnExistsAsync())
            {
                return;
            }

            await _dbContext.Database.ExecuteSqlRawAsync("""
                INSERT INTO ResumeSectionVectors
                    (ResumeSectionId, TenantId, SectionTitle, ParentSectionTitle, Content, EmbeddingPayload, CreatedAt)
                SELECT
                    section.Id,
                    section.TenantId,
                    section.Title,
                    parent.Title,
                    section.Content,
                    section.VectorId,
                    section.CreatedAt
                FROM ResumeSections AS section
                LEFT JOIN ResumeSections AS parent ON parent.Id = section.ParentSectionId
                WHERE section.VectorId IS NOT NULL
                    AND trim(section.VectorId) <> ''
                    AND trim(section.VectorId) LIKE '[%]'
                    AND NOT EXISTS (
                        SELECT 1
                        FROM ResumeSectionVectors AS vector
                        WHERE vector.ResumeSectionId = section.Id
                    );
                """);
        }

        private async Task<bool> LegacyVectorColumnExistsAsync()
        {
            var connection = _dbContext.Database.GetDbConnection();
            var closeConnection = connection.State != System.Data.ConnectionState.Open;
            if (closeConnection)
            {
                await connection.OpenAsync();
            }

            try
            {
                await using var command = connection.CreateCommand();
                command.CommandText = "PRAGMA table_info('ResumeSections');";

                await using var reader = await command.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    if (string.Equals(reader.GetString(reader.GetOrdinal("name")), "VectorId", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }

                return false;
            }
            finally
            {
                if (closeConnection)
                {
                    await connection.CloseAsync();
                }
            }
        }

        private sealed record LexicalQueryTerms(IReadOnlyList<string> EntityTerms, IReadOnlyList<string> IntentTerms);

        private sealed record LexicalScore(double EntityScore, double IntentScore);

        private sealed record ResumeLexicalProfile(IReadOnlySet<string> EntityKeys)
        {
            public bool ContainsEntity(string value)
            {
                return EntityKeys.Contains(NormalizeEntityKey(value));
            }
        }

        private sealed record SearchCandidate(string Content, double VectorScore, double EntityScore, double IntentScore)
        {
            public double RetrievalScore => VectorScore + EntityScore + IntentScore;

            public double DisplayScore => Math.Clamp(RetrievalScore, 0d, 1d);
        }
    }
}
