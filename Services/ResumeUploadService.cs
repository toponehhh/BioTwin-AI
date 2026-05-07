using BioTwin_AI.Data;
using BioTwin_AI.Models;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.EntityFrameworkCore;
using System.Linq;

namespace BioTwin_AI.Services
{
    /// <summary>
    /// Service for handling resume file uploads and conversion to Markdown
    /// </summary>
    public class ResumeUploadService
    {
        private readonly BioTwinDbContext _dbContext;
        private readonly IRagService _ragService;
        private readonly HttpClient _httpClient;
        private readonly ILogger<ResumeUploadService> _logger;
        private readonly string _all2mdApiUrl;
        private readonly CurrentUserSession _session;

        public ResumeUploadService(
            BioTwinDbContext dbContext,
            IRagService ragService,
            HttpClient httpClient,
            ILogger<ResumeUploadService> logger,
            CurrentUserSession session,
            IConfiguration config)
        {
            _dbContext = dbContext;
            _ragService = ragService;
            _httpClient = httpClient;
            _logger = logger;
            _all2mdApiUrl = config["All2MD:ApiUrl"] ?? "http://localhost:8000";
            _session = session;
        }

        private string GetTenantId()
        {
            if (!_session.IsAuthenticated || string.IsNullOrWhiteSpace(_session.Username))
            {
                throw new InvalidOperationException("Please sign in first.");
            }

            return _session.Username;
        }

        /// <summary>
        /// Process uploaded file: convert to MD via All2MD service, store in DB, and index in RAG
        /// </summary>
        public async Task<ResumeEntry> ProcessResumeFileAsync(IBrowserFile file, string title)
        {
            try
            {
                _logger.LogInformation("Processing resume file: {FileName}", file.Name);

                // Step 1: Read file content
                using var stream = file.OpenReadStream(maxAllowedSize: 10 * 1024 * 1024); // 10MB max
                using var memoryStream = new MemoryStream();
                await stream.CopyToAsync(memoryStream);
                var fileBytes = memoryStream.ToArray();

                // Step 2: Convert to Markdown via All2MD service
                var markdownContent = await ConvertToMarkdownAsync(file.Name, fileBytes);

                // Step 3: Store in SQLite
                var tenantId = GetTenantId();
                var entry = new ResumeEntry
                {
                    Title = title,
                    Content = markdownContent,
                    SourceFileName = file.Name,
                    CreatedAt = DateTime.UtcNow,
                    TenantId = tenantId
                };

                _dbContext.ResumeEntries.Add(entry);
                await _dbContext.SaveChangesAsync();

                _logger.LogInformation("Saved resume entry ID: {Id}", entry.Id);

                // Step 4: Index in sqlite-vec for RAG retrieval
                var embeddingPayload = await _ragService.CreateEmbeddingPayloadAsync(
                    markdownContent,
                    new Dictionary<string, string>
                    {
                        { "title", title },
                        { "content", markdownContent },
                        { "db_id", entry.Id.ToString() },
                        { "source_file", file.Name },
                        { "tenant_id", tenantId }
                    }
                );

                entry.EmbeddingPayload = embeddingPayload;
                await _dbContext.SaveChangesAsync();

                _logger.LogInformation("Indexed resume with embedding payload");

                return entry;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process resume file");
                throw;
            }
        }

        /// <summary>
        /// Convert file to Markdown using All2MD service
        /// </summary>
        private async Task<string> ConvertToMarkdownAsync(string filename, byte[] fileBytes)
        {
            try
            {
                using var content = new MultipartFormDataContent();
                using var fileContent = new ByteArrayContent(fileBytes);
                fileContent.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse("application/octet-stream");
                content.Add(fileContent, "file", filename);

                // Call All2MD API /convert/json endpoint
                var response = await _httpClient.PostAsync($"{_all2mdApiUrl}/convert/json", content);

                if (!response.IsSuccessStatusCode)
                {
                    var error = await response.Content.ReadAsStringAsync();
                    _logger.LogWarning("All2MD API error: {StatusCode} - {Error}", response.StatusCode, error);
                    throw new HttpRequestException($"All2MD conversion failed: {response.StatusCode}");
                }

                var jsonResponse = await response.Content.ReadAsStringAsync();

                // Parse JSON response to extract markdown content
                using var doc = System.Text.Json.JsonDocument.Parse(jsonResponse);
                var root = doc.RootElement;

                if (root.TryGetProperty("content", out var contentProp))
                {
                    return contentProp.GetString() ?? "# Document\n\nConversion failed to extract content.";
                }

                return jsonResponse;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to convert file to Markdown");
                throw;
            }
        }

        /// <summary>
        /// Get all resume entries
        /// </summary>
        public async Task<List<ResumeEntry>> GetAllEntriesAsync()
        {
            var tenantId = GetTenantId();
            return await _dbContext.ResumeEntries
                .Where(e => e.TenantId == tenantId)
                .OrderByDescending(e => e.CreatedAt)
                .ToListAsync();
        }

        /// <summary>
        /// Get resume entry by ID
        /// </summary>
        public async Task<ResumeEntry?> GetEntryAsync(int id)
        {
            var tenantId = GetTenantId();
            return await _dbContext.ResumeEntries
                .FirstOrDefaultAsync(e => e.Id == id && e.TenantId == tenantId);
        }

        /// <summary>
        /// Delete resume entry and its vector embeddings
        /// </summary>
        public async Task DeleteEntryAsync(int id)
        {
            var tenantId = GetTenantId();
            var entry = await _dbContext.ResumeEntries
                .FirstOrDefaultAsync(e => e.Id == id && e.TenantId == tenantId);
            if (entry != null)
            {
                _dbContext.ResumeEntries.Remove(entry);
                await _dbContext.SaveChangesAsync();
                _logger.LogInformation("Deleted resume entry ID: {Id} for tenant {TenantId}", id, tenantId);
            }
        }
    }
}
