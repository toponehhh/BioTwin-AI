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
        private readonly RagService _ragService;
        private readonly HttpClient _httpClient;
        private readonly ILogger<ResumeUploadService> _logger;
        private readonly string _all2mdApiUrl;

        public ResumeUploadService(
            BioTwinDbContext dbContext,
            RagService ragService,
            HttpClient httpClient,
            ILogger<ResumeUploadService> logger,
            IConfiguration config)
        {
            _dbContext = dbContext;
            _ragService = ragService;
            _httpClient = httpClient;
            _logger = logger;
            _all2mdApiUrl = config["All2MD:ApiUrl"] ?? "http://localhost:8000";
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
                var entry = new ResumeEntry
                {
                    Title = title,
                    Content = markdownContent,
                    SourceFileName = file.Name,
                    CreatedAt = DateTime.UtcNow
                };

                _dbContext.ResumeEntries.Add(entry);
                await _dbContext.SaveChangesAsync();

                _logger.LogInformation("Saved resume entry ID: {Id}", entry.Id);

                // Step 4: Index in sqlite-vec for RAG retrieval
                var vectorId = await _ragService.StoreEmbeddingAsync(
                    markdownContent,
                    new Dictionary<string, string>
                    {
                        { "title", title },
                        { "content", markdownContent },
                        { "db_id", entry.Id.ToString() },
                        { "source_file", file.Name }
                    }
                );

                entry.VectorId = vectorId;
                await _dbContext.SaveChangesAsync();

                _logger.LogInformation("Indexed resume in sqlite-vec with vector ID: {VectorId}", vectorId);

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
            return await _dbContext.ResumeEntries.OrderByDescending(e => e.CreatedAt).ToListAsync();
        }

        /// <summary>
        /// Get resume entry by ID
        /// </summary>
        public async Task<ResumeEntry?> GetEntryAsync(int id)
        {
            return await _dbContext.ResumeEntries.FirstOrDefaultAsync(e => e.Id == id);
        }

        /// <summary>
        /// Delete resume entry and its vector embeddings
        /// </summary>
        public async Task DeleteEntryAsync(int id)
        {
            var entry = await _dbContext.ResumeEntries.FirstOrDefaultAsync(e => e.Id == id);
            if (entry != null)
            {
                _dbContext.ResumeEntries.Remove(entry);
                await _dbContext.SaveChangesAsync();
                _logger.LogInformation("Deleted resume entry ID: {Id}", id);
            }
        }
    }
}
