using BioTwin_AI.Data;
using BioTwin_AI.Models;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace BioTwin_AI.Services
{
    public sealed record ConvertedResumeFile(
        string MarkdownContent,
        string FileName,
        string ContentType,
        long Size,
        byte[] FileBytes,
        string FileHash,
        bool IsDuplicate = false,
        int? ExistingResumeEntryId = null,
        string? ExistingResumeTitle = null);

    public sealed record ResumeMarkdownSection(
        string Title,
        string Content,
        int HeadingLevel,
        int? ParentIndex);

    /// <summary>
    /// Service for handling resume file uploads and conversion to Markdown.
    /// </summary>
    public class ResumeUploadService
    {
        private readonly BioTwinDbContext _dbContext;
        private readonly IRagService _ragService;
        private readonly ResumeMarkdownRefinementService _markdownRefinementService;
        private readonly HttpClient _httpClient;
        private readonly ILogger<ResumeUploadService> _logger;
        private readonly string _all2mdApiUrl;
        private readonly CurrentUserSession _session;

        public ResumeUploadService(
            BioTwinDbContext dbContext,
            IRagService ragService,
            ResumeMarkdownRefinementService markdownRefinementService,
            HttpClient httpClient,
            ILogger<ResumeUploadService> logger,
            CurrentUserSession session,
            IConfiguration config)
        {
            _dbContext = dbContext;
            _ragService = ragService;
            _markdownRefinementService = markdownRefinementService;
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
        /// Process uploaded file: convert to MD via All2MD service, store in DB, and index in RAG.
        /// </summary>
        public async Task<ResumeEntry> ProcessResumeFileAsync(
            IBrowserFile file,
            string title,
            Func<string, Task>? progress = null)
        {
            var convertedFile = await ConvertResumeFileAsync(file, progress);
            if (convertedFile.IsDuplicate && convertedFile.ExistingResumeEntryId is not null)
            {
                return await _dbContext.ResumeEntries
                    .FirstAsync(entry => entry.Id == convertedFile.ExistingResumeEntryId.Value);
            }

            var sections = await SaveResumeMarkdownSectionsAsync(
                title,
                convertedFile.MarkdownContent,
                convertedFile.FileName,
                convertedFile.ContentType,
                convertedFile.Size,
                convertedFile.FileBytes,
                convertedFile.FileHash,
                progress);

            return sections.First().ResumeEntry
                ?? await _dbContext.ResumeEntries.FirstAsync(entry => entry.Id == sections.First().ResumeEntryId);
        }

        /// <summary>
        /// Convert an uploaded file to Markdown via All2MD without saving it.
        /// </summary>
        public async Task<string> ConvertResumeFileToMarkdownAsync(
            IBrowserFile file,
            Func<string, Task>? progress = null)
        {
            var convertedFile = await ConvertResumeFileAsync(file, progress);
            return convertedFile.MarkdownContent;
        }

        /// <summary>
        /// Convert an uploaded file to Markdown and keep the original file bytes for later persistence.
        /// </summary>
        public async Task<ConvertedResumeFile> ConvertResumeFileAsync(
            IBrowserFile file,
            Func<string, Task>? progress = null)
        {
            try
            {
                _logger.LogInformation("Processing resume file: {FileName}", file.Name);
                await ReportProgressAsync(progress, $"Reading uploaded file: {file.Name}");

                using var stream = file.OpenReadStream(maxAllowedSize: 10 * 1024 * 1024);
                using var memoryStream = new MemoryStream();
                await stream.CopyToAsync(memoryStream);
                var fileBytes = memoryStream.ToArray();
                var fileHash = ComputeFileHash(fileBytes);
                var existingEntry = await FindExistingResumeByHashAsync(fileHash);
                if (existingEntry is not null)
                {
                    await ReportProgressAsync(progress, $"This file already exists as {existingEntry.SourceFileName}. Skipping conversion and upload.");
                    var existingMarkdown = await BuildMarkdownForEntryAsync(existingEntry.Id);
                    return new ConvertedResumeFile(
                        existingMarkdown,
                        existingEntry.SourceFileName,
                        existingEntry.SourceContentType ?? file.ContentType,
                        existingEntry.SourceFileSize ?? file.Size,
                        existingEntry.SourceFileContent ?? fileBytes,
                        fileHash,
                        true,
                        existingEntry.Id,
                        existingEntry.Title);
                }

                await ReportProgressAsync(progress, $"Converting {GetFileKind(file.Name)} to Markdown. This can take a few minutes...");
                var markdownContent = await ConvertToMarkdownAsync(file.Name, fileBytes, progress);
                await ReportProgressAsync(progress, "Markdown conversion completed. Refining section structure...");
                markdownContent = await _markdownRefinementService.RefineAsync(
                    markdownContent,
                    Path.GetFileNameWithoutExtension(file.Name),
                    progress);

                return new ConvertedResumeFile(
                    markdownContent,
                    file.Name,
                    file.ContentType,
                    file.Size,
                    fileBytes,
                    fileHash);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to convert resume file");
                throw;
            }
        }

        /// <summary>
        /// Store edited Markdown as a single resume section and index it for RAG retrieval.
        /// </summary>
        public async Task<ResumeEntry> SaveResumeMarkdownAsync(string title, string markdownContent, string sourceFileName)
        {
            return await SaveResumeMarkdownAsync(title, markdownContent, sourceFileName, null, null, null, null);
        }

        /// <summary>
        /// Store the source resume file once, split its converted Markdown into sections, and index each section.
        /// </summary>
        public async Task<List<ResumeSection>> SaveResumeMarkdownSectionsAsync(
            string fallbackTitle,
            string markdownContent,
            string sourceFileName,
            string? sourceContentType,
            long? sourceFileSize,
            byte[]? sourceFileContent,
            string? sourceFileHash,
            Func<string, Task>? progress = null)
        {
            try
            {
                var tenantId = GetTenantId();
                if (!string.IsNullOrWhiteSpace(sourceFileHash))
                {
                    var existingSections = await LoadExistingSectionsByHashAsync(sourceFileHash, tenantId);
                    if (existingSections.Count > 0)
                    {
                        await ReportProgressAsync(progress, "This file was already uploaded. Checking existing section embeddings...");
                        await EnsureSectionEmbeddingsAsync(existingSections, sourceFileName, progress);
                        await _dbContext.SaveChangesAsync();
                        await ReportProgressAsync(progress, "Reusing the existing indexed resume.");
                        return existingSections;
                    }
                }

                await ReportProgressAsync(progress, "Splitting Markdown content into sections...");
                var sections = SplitResumeMarkdown(markdownContent, fallbackTitle);
                var createdAt = DateTime.UtcNow;
                await ReportProgressAsync(progress, $"Markdown was split into {sections.Count} section(s). Saving original resume file...");

                var resumeEntry = new ResumeEntry
                {
                    Title = string.IsNullOrWhiteSpace(fallbackTitle) ? Path.GetFileNameWithoutExtension(sourceFileName) : fallbackTitle.Trim(),
                    SourceFileName = sourceFileName,
                    SourceContentType = sourceContentType,
                    SourceFileSize = sourceFileSize,
                    SourceFileContent = sourceFileContent,
                    SourceFileHash = sourceFileHash,
                    CreatedAt = createdAt,
                    TenantId = tenantId
                };

                _dbContext.ResumeEntries.Add(resumeEntry);
                await _dbContext.SaveChangesAsync();

                await ReportProgressAsync(progress, "Original resume file saved. Writing sections...");
                var sectionEntries = sections
                    .Select((section, index) => new ResumeSection
                    {
                        ResumeEntryId = resumeEntry.Id,
                        ResumeEntry = resumeEntry,
                        Title = section.Title,
                        Content = section.Content,
                        HeadingLevel = section.HeadingLevel,
                        SortOrder = index,
                        CreatedAt = createdAt.AddTicks(index),
                        TenantId = tenantId
                    })
                    .ToList();

                _dbContext.ResumeSections.AddRange(sectionEntries);
                await _dbContext.SaveChangesAsync();

                for (var i = 0; i < sections.Count; i++)
                {
                    var parentIndex = sections[i].ParentIndex;
                    if (parentIndex is not null && parentIndex.Value >= 0 && parentIndex.Value < sectionEntries.Count)
                    {
                        sectionEntries[i].ParentSectionId = sectionEntries[parentIndex.Value].Id;
                    }
                }

                await _dbContext.SaveChangesAsync();

                await EnsureSectionEmbeddingsAsync(sectionEntries, sourceFileName, progress);
                await _dbContext.SaveChangesAsync();

                await ReportProgressAsync(progress, $"Done. Saved and indexed {sectionEntries.Count} section(s).");
                _logger.LogInformation("Saved {Count} resume sections from {SourceFile}", sectionEntries.Count, sourceFileName);
                return sectionEntries;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save resume sections");
                throw;
            }
        }

        /// <summary>
        /// Replace an existing resume's editable sections with reviewed Markdown and rebuild embeddings.
        /// </summary>
        public async Task<List<ResumeSection>> ReplaceResumeMarkdownSectionsAsync(
            int resumeEntryId,
            string fallbackTitle,
            string markdownContent,
            Func<string, Task>? progress = null)
        {
            try
            {
                var tenantId = GetTenantId();
                var resumeEntry = await _dbContext.ResumeEntries
                    .Include(entry => entry.Sections)
                    .FirstOrDefaultAsync(entry => entry.Id == resumeEntryId && entry.TenantId == tenantId);

                if (resumeEntry is null)
                {
                    throw new InvalidOperationException("The selected resume could not be found.");
                }

                await ReportProgressAsync(progress, "Splitting updated Markdown content into sections...");
                var sections = SplitResumeMarkdown(markdownContent, fallbackTitle);
                var createdAt = DateTime.UtcNow;

                resumeEntry.Title = string.IsNullOrWhiteSpace(fallbackTitle)
                    ? resumeEntry.Title
                    : fallbackTitle.Trim();

                await ReportProgressAsync(progress, "Replacing existing sections...");
                _dbContext.ResumeSections.RemoveRange(resumeEntry.Sections);
                await _dbContext.SaveChangesAsync();
                resumeEntry.Sections.Clear();

                var sectionEntries = sections
                    .Select((section, index) => new ResumeSection
                    {
                        ResumeEntryId = resumeEntry.Id,
                        ResumeEntry = resumeEntry,
                        Title = section.Title,
                        Content = section.Content,
                        HeadingLevel = section.HeadingLevel,
                        SortOrder = index,
                        CreatedAt = createdAt.AddTicks(index),
                        TenantId = tenantId
                    })
                    .ToList();

                _dbContext.ResumeSections.AddRange(sectionEntries);
                await _dbContext.SaveChangesAsync();

                for (var i = 0; i < sections.Count; i++)
                {
                    var parentIndex = sections[i].ParentIndex;
                    if (parentIndex is not null && parentIndex.Value >= 0 && parentIndex.Value < sectionEntries.Count)
                    {
                        sectionEntries[i].ParentSectionId = sectionEntries[parentIndex.Value].Id;
                    }
                }

                await _dbContext.SaveChangesAsync();

                await EnsureSectionEmbeddingsAsync(sectionEntries, resumeEntry.SourceFileName, progress);
                await _dbContext.SaveChangesAsync();

                await ReportProgressAsync(progress, $"Done. Updated and indexed {sectionEntries.Count} section(s).");
                _logger.LogInformation("Replaced {Count} resume sections for entry {ResumeEntryId}", sectionEntries.Count, resumeEntry.Id);
                return sectionEntries;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to replace resume sections");
                throw;
            }
        }

        /// <summary>
        /// Store edited Markdown plus original uploaded file bytes in SQLite and index it for RAG retrieval.
        /// </summary>
        public async Task<ResumeEntry> SaveResumeMarkdownAsync(
            string title,
            string markdownContent,
            string sourceFileName,
            string? sourceContentType,
            long? sourceFileSize,
            byte[]? sourceFileContent,
            string? sourceFileHash = null)
        {
            try
            {
                var tenantId = GetTenantId();
                if (!string.IsNullOrWhiteSpace(sourceFileHash))
                {
                    var existingEntry = await FindExistingResumeByHashAsync(sourceFileHash);
                    if (existingEntry is not null)
                    {
                        return existingEntry;
                    }
                }

                var createdAt = DateTime.UtcNow;
                var entry = new ResumeEntry
                {
                    Title = string.IsNullOrWhiteSpace(title) ? Path.GetFileNameWithoutExtension(sourceFileName) : title.Trim(),
                    SourceFileName = sourceFileName,
                    SourceContentType = sourceContentType,
                    SourceFileSize = sourceFileSize,
                    SourceFileContent = sourceFileContent,
                    SourceFileHash = sourceFileHash,
                    CreatedAt = createdAt,
                    TenantId = tenantId
                };

                _dbContext.ResumeEntries.Add(entry);
                await _dbContext.SaveChangesAsync();

                _logger.LogInformation("Saved resume entry ID: {Id}", entry.Id);

                var section = new ResumeSection
                {
                    ResumeEntryId = entry.Id,
                    ResumeEntry = entry,
                    Title = title,
                    Content = markdownContent,
                    HeadingLevel = 2,
                    SortOrder = 0,
                    CreatedAt = createdAt,
                    TenantId = tenantId
                };

                _dbContext.ResumeSections.Add(section);
                await _dbContext.SaveChangesAsync();

                section.EmbeddingPayload = await _ragService.CreateEmbeddingPayloadAsync(
                    markdownContent,
                    new Dictionary<string, string>
                    {
                        { "title", title },
                        { "content", markdownContent },
                        { "db_id", section.Id.ToString() },
                        { "resume_entry_id", entry.Id.ToString() },
                        { "source_file", sourceFileName },
                        { "tenant_id", tenantId }
                    });

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

        private async Task<ResumeEntry?> FindExistingResumeByHashAsync(string fileHash)
        {
            var tenantId = GetTenantId();
            return await _dbContext.ResumeEntries
                .AsNoTracking()
                .FirstOrDefaultAsync(entry => entry.TenantId == tenantId && entry.SourceFileHash == fileHash);
        }

        private async Task<List<ResumeSection>> LoadExistingSectionsByHashAsync(string fileHash, string tenantId)
        {
            return await _dbContext.ResumeSections
                .Include(section => section.ResumeEntry)
                .Where(section =>
                    section.TenantId == tenantId &&
                    section.ResumeEntry != null &&
                    section.ResumeEntry.SourceFileHash == fileHash)
                .OrderBy(section => section.SortOrder)
                .ToListAsync();
        }

        private async Task EnsureSectionEmbeddingsAsync(
            IReadOnlyList<ResumeSection> sectionEntries,
            string sourceFileName,
            Func<string, Task>? progress)
        {
            for (var i = 0; i < sectionEntries.Count; i++)
            {
                var sectionEntry = sectionEntries[i];
                if (!string.IsNullOrWhiteSpace(sectionEntry.EmbeddingPayload))
                {
                    continue;
                }

                await ReportProgressAsync(progress, $"Processing section {i + 1} / {sectionEntries.Count}: {sectionEntry.Title}");
                sectionEntry.EmbeddingPayload = await _ragService.CreateEmbeddingPayloadAsync(
                    sectionEntry.Content,
                    new Dictionary<string, string>
                    {
                        { "title", sectionEntry.Title },
                        { "content", sectionEntry.Content },
                        { "db_id", sectionEntry.Id.ToString() },
                        { "resume_entry_id", sectionEntry.ResumeEntryId.ToString() },
                        { "source_file", sourceFileName },
                        { "tenant_id", sectionEntry.TenantId }
                    });
            }
        }

        private async Task<string> BuildMarkdownForEntryAsync(int resumeEntryId)
        {
            var tenantId = GetTenantId();
            var sections = await _dbContext.ResumeSections
                .AsNoTracking()
                .Where(section => section.TenantId == tenantId && section.ResumeEntryId == resumeEntryId)
                .OrderBy(section => section.SortOrder)
                .ToListAsync();

            var markdown = new StringBuilder();
            foreach (var section in sections)
            {
                markdown.AppendLine($"{new string('#', Math.Clamp(section.HeadingLevel, 1, 6))} {section.Title}");
                markdown.AppendLine();
                markdown.AppendLine(section.Content);
                markdown.AppendLine();
            }

            return markdown.ToString().Trim();
        }

        private static string ComputeFileHash(byte[] fileBytes)
        {
            return Convert.ToHexString(SHA256.HashData(fileBytes)).ToLowerInvariant();
        }

        /// <summary>
        /// Convert file to Markdown using All2MD job polling when available.
        /// </summary>
        private async Task<string> ConvertToMarkdownAsync(
            string filename,
            byte[] fileBytes,
            Func<string, Task>? progress)
        {
            if (await TryStartConversionJobAsync(filename, fileBytes, progress) is { } jobResult)
            {
                return jobResult;
            }

            await ReportProgressAsync(progress, "All2MD progress API is not available. Falling back to synchronous conversion...");
            return await ConvertToMarkdownSynchronouslyAsync(filename, fileBytes);
        }

        private async Task<string?> TryStartConversionJobAsync(
            string filename,
            byte[] fileBytes,
            Func<string, Task>? progress)
        {
            using var content = CreateMultipartContent(filename, fileBytes);
            using var response = await _httpClient.PostAsync($"{_all2mdApiUrl}/convert/jobs", content);

            if (response.StatusCode == System.Net.HttpStatusCode.NotFound ||
                response.StatusCode == System.Net.HttpStatusCode.MethodNotAllowed)
            {
                return null;
            }

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                throw new HttpRequestException($"All2MD job start failed: {response.StatusCode} - {error}");
            }

            using var startDoc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            if (!startDoc.RootElement.TryGetProperty("job_id", out var jobIdProp))
            {
                throw new InvalidOperationException("All2MD job response did not include a job_id.");
            }

            var jobId = jobIdProp.GetString();
            if (string.IsNullOrWhiteSpace(jobId))
            {
                throw new InvalidOperationException("All2MD returned an empty job_id.");
            }

            await ReportProgressAsync(progress, $"All2MD job queued: {jobId}");

            while (true)
            {
                await Task.Delay(TimeSpan.FromSeconds(2));
                using var statusResponse = await _httpClient.GetAsync($"{_all2mdApiUrl}/convert/jobs/{jobId}");
                if (!statusResponse.IsSuccessStatusCode)
                {
                    var error = await statusResponse.Content.ReadAsStringAsync();
                    throw new HttpRequestException($"All2MD job status failed: {statusResponse.StatusCode} - {error}");
                }

                using var statusDoc = JsonDocument.Parse(await statusResponse.Content.ReadAsStringAsync());
                var root = statusDoc.RootElement;
                var status = root.TryGetProperty("status", out var statusProp)
                    ? statusProp.GetString()
                    : "unknown";
                var message = root.TryGetProperty("message", out var messageProp)
                    ? messageProp.GetString()
                    : "Converting document";
                var percent = root.TryGetProperty("progress", out var progressProp)
                    ? progressProp.GetInt32()
                    : 0;

                await ReportProgressAsync(progress, $"All2MD: {message} ({percent}%)");

                if (string.Equals(status, "completed", StringComparison.OrdinalIgnoreCase))
                {
                    if (root.TryGetProperty("result", out var resultProp) &&
                        resultProp.TryGetProperty("content", out var contentProp))
                    {
                        return contentProp.GetString() ?? string.Empty;
                    }

                    throw new InvalidOperationException("All2MD job completed without Markdown content.");
                }

                if (string.Equals(status, "failed", StringComparison.OrdinalIgnoreCase))
                {
                    var error = root.TryGetProperty("error", out var errorProp)
                        ? errorProp.GetString()
                        : message;
                    throw new InvalidOperationException($"All2MD conversion failed: {error}");
                }
            }
        }

        private async Task<string> ConvertToMarkdownSynchronouslyAsync(string filename, byte[] fileBytes)
        {
            using var content = CreateMultipartContent(filename, fileBytes);
            using var response = await _httpClient.PostAsync($"{_all2mdApiUrl}/convert/json", content);

            if (!response.IsSuccessStatusCode)
            {
                var error = await response.Content.ReadAsStringAsync();
                _logger.LogWarning("All2MD API error: {StatusCode} - {Error}", response.StatusCode, error);
                throw new HttpRequestException($"All2MD conversion failed: {response.StatusCode}");
            }

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return doc.RootElement.TryGetProperty("content", out var contentProp)
                ? contentProp.GetString() ?? "# Document\n\nConversion failed to extract content."
                : doc.RootElement.GetRawText();
        }

        private static MultipartFormDataContent CreateMultipartContent(string filename, byte[] fileBytes)
        {
            var content = new MultipartFormDataContent();
            var fileContent = new ByteArrayContent(fileBytes);
            fileContent.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse("application/octet-stream");
            content.Add(fileContent, "file", filename);
            return content;
        }

        public static List<ResumeMarkdownSection> SplitResumeMarkdown(string markdownContent, string fallbackTitle)
        {
            var fallback = string.IsNullOrWhiteSpace(fallbackTitle) ? "Resume" : fallbackTitle.Trim();
            var normalized = (markdownContent ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');

            if (string.IsNullOrWhiteSpace(normalized))
            {
                return new List<ResumeMarkdownSection>
                {
                    new(fallback, string.Empty, 1, null)
                };
            }

            var lines = normalized.Split('\n');
            var headings = lines
                .Select((line, index) => new { Line = line, Index = index, Match = Regex.Match(line, @"^\s{0,3}(#{1,6})\s+(.+?)\s*#*\s*$") })
                .Where(item => item.Match.Success)
                .Select(item => new
                {
                    item.Index,
                    Level = item.Match.Groups[1].Value.Length,
                    Title = CleanMarkdownHeading(item.Match.Groups[2].Value)
                })
                .Where(item => !string.IsNullOrWhiteSpace(item.Title))
                .ToList();

            if (headings.Count == 0)
            {
                return new List<ResumeMarkdownSection>
                {
                    new(fallback, normalized.Trim(), 1, null)
                };
            }

            var sections = new List<ResumeMarkdownSection>();
            var headingToSectionIndex = new Dictionary<int, int>();
            var ancestorByLevel = new Dictionary<int, int>();

            if (headings[0].Index > 0)
            {
                var intro = string.Join('\n', lines.Take(headings[0].Index)).Trim();
                if (!string.IsNullOrWhiteSpace(intro))
                {
                    sections.Add(new ResumeMarkdownSection(fallback, intro, 1, null));
                }
            }

            for (var i = 0; i < headings.Count; i++)
            {
                var current = headings[i];
                var nextIndex = i + 1 < headings.Count ? headings[i + 1].Index : lines.Length;
                var body = string.Join('\n', lines.Skip(current.Index + 1).Take(nextIndex - current.Index - 1)).Trim();
                int? parentIndex = null;

                for (var level = current.Level - 1; level >= 1; level--)
                {
                    if (ancestorByLevel.TryGetValue(level, out var ancestorHeadingIndex) &&
                        headingToSectionIndex.TryGetValue(ancestorHeadingIndex, out var ancestorSectionIndex))
                    {
                        parentIndex = ancestorSectionIndex;
                        break;
                    }
                }

                var sectionIndex = sections.Count;
                sections.Add(new ResumeMarkdownSection(current.Title, body, current.Level, parentIndex));
                headingToSectionIndex[i] = sectionIndex;
                ancestorByLevel[current.Level] = i;

                foreach (var staleLevel in ancestorByLevel.Keys.Where(level => level > current.Level).ToList())
                {
                    ancestorByLevel.Remove(staleLevel);
                }
            }

            return sections
                .Where(section => !string.IsNullOrWhiteSpace(section.Title) || !string.IsNullOrWhiteSpace(section.Content))
                .Select(section => new ResumeMarkdownSection(
                    string.IsNullOrWhiteSpace(section.Title) ? fallback : section.Title.Trim(),
                    section.Content.Trim(),
                    Math.Clamp(section.HeadingLevel, 1, 6),
                    section.ParentIndex))
                .ToList();
        }

        private static string CleanMarkdownHeading(string heading)
        {
            return Regex.Replace(heading.Trim(), @"\s+", " ");
        }

        private static Task ReportProgressAsync(Func<string, Task>? progress, string message)
        {
            return progress is null ? Task.CompletedTask : progress(message);
        }

        private static string GetFileKind(string fileName)
        {
            var extension = Path.GetExtension(fileName).ToLowerInvariant();
            return extension switch
            {
                ".pdf" => "PDF",
                ".docx" => "DOCX",
                ".pptx" => "PPTX",
                ".html" or ".htm" => "HTML",
                ".txt" => "TXT",
                _ => "file"
            };
        }

        /// <summary>
        /// Get all resume source entries.
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
        /// Get all editable resume sections.
        /// </summary>
        public async Task<List<ResumeSection>> GetAllSectionsAsync()
        {
            var tenantId = GetTenantId();
            return await _dbContext.ResumeSections
                .Include(section => section.ResumeEntry)
                .Where(section => section.TenantId == tenantId)
                .OrderByDescending(section => section.ResumeEntry != null ? section.ResumeEntry.CreatedAt : section.CreatedAt)
                .ThenBy(section => section.SortOrder)
                .ToListAsync();
        }

        /// <summary>
        /// Get resume entry by ID.
        /// </summary>
        public async Task<ResumeEntry?> GetEntryAsync(int id)
        {
            var tenantId = GetTenantId();
            return await _dbContext.ResumeEntries
                .FirstOrDefaultAsync(e => e.Id == id && e.TenantId == tenantId);
        }

        /// <summary>
        /// Delete a resume source entry and its sections.
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
