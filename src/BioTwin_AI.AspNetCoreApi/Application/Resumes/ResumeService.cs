using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BioTwin_AI.AspNetCoreApi.Application.Embeddings;
using BioTwin_AI.AspNetCoreApi.Infrastructure.Data;
using BioTwin_AI.AspNetCoreApi.Infrastructure.Data.Entities;
using BioTwin_AI.DotNetShared.Resumes;
using Microsoft.EntityFrameworkCore;

namespace BioTwin_AI.AspNetCoreApi.Application.Resumes;

public sealed class ResumeService(
    BioTwinApiDbContext dbContext,
    IEmbeddingService embeddingService,
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    ILogger<ResumeService> logger) : IResumeService
{
    private const long MaxUploadBytes = 10 * 1024 * 1024;

    public async Task<IReadOnlyList<ResumeSummaryDto>> GetSummariesAsync(string tenantId, CancellationToken cancellationToken)
    {
        var entries = await dbContext.ResumeEntries
            .AsNoTracking()
            .Include(entry => entry.Sections)
            .Where(entry => entry.TenantId == tenantId)
            .ToListAsync(cancellationToken);

        return entries
            .OrderByDescending(entry => entry.CreatedAt)
            .Select(entry => new ResumeSummaryDto(
                entry.Id,
                entry.Title,
                entry.SourceFileName,
                entry.CreatedAt,
                entry.Sections.Count,
                entry.SourceFileContent != null))
            .ToArray();
    }

    public async Task<ResumeDetailDto?> GetDetailAsync(string tenantId, int resumeId, CancellationToken cancellationToken)
    {
        var entry = await dbContext.ResumeEntries
            .AsNoTracking()
            .Include(resume => resume.Sections)
            .FirstOrDefaultAsync(resume => resume.TenantId == tenantId && resume.Id == resumeId, cancellationToken);

        return entry is null ? null : ToDetail(entry);
    }

    public async Task<ConvertedResumeFileDto> ConvertUploadAsync(string tenantId, IFormFile file, CancellationToken cancellationToken)
    {
        if (file.Length <= 0)
        {
            throw new InvalidOperationException("Uploaded file is empty.");
        }

        if (file.Length > MaxUploadBytes)
        {
            throw new InvalidOperationException("Uploaded file is larger than the 10 MB limit.");
        }

        await using var input = file.OpenReadStream();
        using var memory = new MemoryStream();
        await input.CopyToAsync(memory, cancellationToken);
        var bytes = memory.ToArray();
        var hash = ComputeHash(bytes);
        var duplicate = await dbContext.ResumeEntries
            .AsNoTracking()
            .FirstOrDefaultAsync(entry => entry.TenantId == tenantId && entry.SourceFileHash == hash, cancellationToken);

        if (duplicate is not null)
        {
            var existingMarkdown = await ExportMarkdownAsync(tenantId, duplicate.Id, cancellationToken);
            return new ConvertedResumeFileDto(
                duplicate.Title,
                duplicate.SourceFileName ?? file.FileName,
                existingMarkdown?.Markdown ?? string.Empty,
                IsDuplicate: true,
                duplicate.Id,
                duplicate.Title);
        }

        var markdown = await ConvertBytesToMarkdownAsync(file.FileName, file.ContentType, bytes, cancellationToken);
        return new ConvertedResumeFileDto(
            Path.GetFileNameWithoutExtension(file.FileName),
            file.FileName,
            markdown,
            IsDuplicate: false,
            ExistingResumeEntryId: null,
            ExistingResumeTitle: null);
    }

    public async Task<ResumeDetailDto> SaveAsync(string tenantId, SaveResumeMarkdownRequest request, CancellationToken cancellationToken)
    {
        var sourceBytes = DecodeOptionalBase64(request.SourceFileContentBase64);
        var sourceHash = sourceBytes is null ? null : ComputeHash(sourceBytes);

        if (!string.IsNullOrWhiteSpace(sourceHash))
        {
            var duplicate = await dbContext.ResumeEntries
                .Include(entry => entry.Sections)
                .FirstOrDefaultAsync(entry => entry.TenantId == tenantId && entry.SourceFileHash == sourceHash, cancellationToken);
            if (duplicate is not null)
            {
                return ToDetail(duplicate);
            }
        }

        var now = DateTimeOffset.UtcNow;
        var entry = new ResumeEntry
        {
            TenantId = tenantId,
            Title = NormalizeTitle(request.Title, request.SourceFileName),
            SourceFileName = request.SourceFileName,
            SourceContentType = request.SourceContentType,
            SourceFileSize = request.SourceFileSize,
            SourceFileContent = sourceBytes,
            SourceFileHash = sourceHash,
            CreatedAt = now,
            UpdatedAt = now
        };

        dbContext.ResumeEntries.Add(entry);
        await dbContext.SaveChangesAsync(cancellationToken);
        await ReplaceSectionsAsync(entry, request.Markdown, cancellationToken);
        return ToDetail(entry);
    }

    public async Task<ResumeDetailDto?> ReplaceMarkdownAsync(string tenantId, int resumeId, SaveResumeMarkdownRequest request, CancellationToken cancellationToken)
    {
        var entry = await dbContext.ResumeEntries
            .Include(resume => resume.Sections)
            .ThenInclude(section => section.Vector)
            .FirstOrDefaultAsync(resume => resume.TenantId == tenantId && resume.Id == resumeId, cancellationToken);

        if (entry is null)
        {
            return null;
        }

        entry.Title = NormalizeTitle(request.Title, entry.SourceFileName);
        entry.SourceFileName = request.SourceFileName ?? entry.SourceFileName;
        entry.SourceContentType = request.SourceContentType ?? entry.SourceContentType;
        entry.SourceFileSize = request.SourceFileSize ?? entry.SourceFileSize;
        entry.UpdatedAt = DateTimeOffset.UtcNow;

        var sourceBytes = DecodeOptionalBase64(request.SourceFileContentBase64);
        if (sourceBytes is not null)
        {
            entry.SourceFileContent = sourceBytes;
            entry.SourceFileHash = ComputeHash(sourceBytes);
        }

        await ReplaceSectionsAsync(entry, request.Markdown, cancellationToken);
        return ToDetail(entry);
    }

    public async Task<bool> DeleteAsync(string tenantId, int resumeId, CancellationToken cancellationToken)
    {
        var entry = await dbContext.ResumeEntries
            .FirstOrDefaultAsync(resume => resume.TenantId == tenantId && resume.Id == resumeId, cancellationToken);
        if (entry is null)
        {
            return false;
        }

        dbContext.ResumeEntries.Remove(entry);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<RebuildEmbeddingsResponse> RebuildEmbeddingsAsync(string tenantId, CancellationToken cancellationToken)
    {
        var entries = await dbContext.ResumeEntries
            .Include(entry => entry.Sections)
            .ThenInclude(section => section.Vector)
            .Where(entry => entry.TenantId == tenantId)
            .ToListAsync(cancellationToken);

        var sectionCount = 0;
        foreach (var entry in entries)
        {
            foreach (var section in entry.Sections)
            {
                await UpsertVectorAsync(entry, section, cancellationToken);
                sectionCount++;
            }
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return new RebuildEmbeddingsResponse(entries.Count, sectionCount);
    }

    public async Task<ResumeMarkdownExportDto?> ExportMarkdownAsync(string tenantId, int resumeId, CancellationToken cancellationToken)
    {
        var entry = await dbContext.ResumeEntries
            .AsNoTracking()
            .Include(resume => resume.Sections)
            .FirstOrDefaultAsync(resume => resume.TenantId == tenantId && resume.Id == resumeId, cancellationToken);

        if (entry is null)
        {
            return null;
        }

        var fileName = $"{SanitizeFileName(entry.Title)}.md";
        return new ResumeMarkdownExportDto(fileName, ResumeMarkdownBuilder.Build(entry.Sections, entry.Title));
    }

    public async Task<(string FileName, string ContentType, byte[] Content)?> GetOriginalAsync(string tenantId, int resumeId, CancellationToken cancellationToken)
    {
        var entry = await dbContext.ResumeEntries
            .AsNoTracking()
            .FirstOrDefaultAsync(resume => resume.TenantId == tenantId && resume.Id == resumeId, cancellationToken);

        if (entry?.SourceFileContent is null)
        {
            return null;
        }

        return (
            entry.SourceFileName ?? $"{SanitizeFileName(entry.Title)}.bin",
            entry.SourceContentType ?? "application/octet-stream",
            entry.SourceFileContent);
    }

    private async Task ReplaceSectionsAsync(ResumeEntry entry, string markdown, CancellationToken cancellationToken)
    {
        var oldSections = await dbContext.ResumeSections
            .Where(section => section.ResumeEntryId == entry.Id)
            .ToListAsync(cancellationToken);

        dbContext.ResumeSections.RemoveRange(oldSections);
        await dbContext.SaveChangesAsync(cancellationToken);

        var parsed = ResumeMarkdownParser.Split(markdown, entry.Title);
        var sectionEntities = parsed
            .Select((section, index) => new ResumeSection
            {
                ResumeEntryId = entry.Id,
                ResumeEntry = entry,
                TenantId = entry.TenantId,
                HeadingLevel = section.HeadingLevel,
                Title = section.Title,
                Content = section.Content,
                SortOrder = index,
                CreatedAt = entry.CreatedAt.AddTicks(index),
                UpdatedAt = entry.UpdatedAt.AddTicks(index)
            })
            .ToList();

        dbContext.ResumeSections.AddRange(sectionEntities);
        await dbContext.SaveChangesAsync(cancellationToken);

        for (var i = 0; i < parsed.Count; i++)
        {
            if (parsed[i].ParentIndex is { } parentIndex && parentIndex >= 0 && parentIndex < sectionEntities.Count)
            {
                sectionEntities[i].ParentSectionId = sectionEntities[parentIndex].Id;
            }
        }

        foreach (var section in sectionEntities)
        {
            await UpsertVectorAsync(entry, section, cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        entry.Sections = sectionEntities;
    }

    private async Task UpsertVectorAsync(ResumeEntry entry, ResumeSection section, CancellationToken cancellationToken)
    {
        var input = $"{entry.Title}\n{section.Title}\n{section.Content}";
        var embedding = await embeddingService.EmbedAsync(input, cancellationToken);
        var payload = EmbeddingPayloadSerializer.Serialize(embedding);

        var vector = section.Vector ?? await dbContext.ResumeSectionVectors
            .FirstOrDefaultAsync(item => item.ResumeSectionId == section.Id, cancellationToken);

        if (vector is null)
        {
            vector = new ResumeSectionVector
            {
                ResumeSectionId = section.Id,
                ResumeSection = section,
                TenantId = entry.TenantId,
                CreatedAt = DateTimeOffset.UtcNow,
                UpdatedAt = DateTimeOffset.UtcNow
            };
            dbContext.ResumeSectionVectors.Add(vector);
        }

        vector.ResumeTitle = entry.Title;
        vector.SectionTitle = section.Title;
        vector.Content = section.Content;
        vector.EmbeddingPayload = payload;
        vector.UpdatedAt = DateTimeOffset.UtcNow;
    }

    private async Task<string> ConvertBytesToMarkdownAsync(string fileName, string? contentType, byte[] bytes, CancellationToken cancellationToken)
    {
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        if (extension is ".md" or ".markdown" or ".txt" || (contentType?.StartsWith("text/", StringComparison.OrdinalIgnoreCase) ?? false))
        {
            return Encoding.UTF8.GetString(bytes);
        }

        try
        {
            return await ConvertWithAll2MdAsync(fileName, bytes, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "All2MD conversion failed for {FileName}; returning placeholder Markdown.", fileName);
            return $"# {Path.GetFileNameWithoutExtension(fileName)}\n\nUploaded file `{fileName}` was received, but automatic conversion is unavailable. Paste or edit the Markdown content before saving.";
        }
    }

    private async Task<string> ConvertWithAll2MdAsync(string fileName, byte[] bytes, CancellationToken cancellationToken)
    {
        var apiUrl = configuration["All2MD:ApiUrl"];
        if (string.IsNullOrWhiteSpace(apiUrl))
        {
            throw new InvalidOperationException("All2MD:ApiUrl is not configured.");
        }

        var client = httpClientFactory.CreateClient("all2md");
        using var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(bytes), "file", fileName);
        using var response = await client.PostAsync($"{apiUrl.TrimEnd('/')}/convert/json", content, cancellationToken);
        response.EnsureSuccessStatusCode();

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        return document.RootElement.TryGetProperty("content", out var contentElement)
            ? contentElement.GetString() ?? string.Empty
            : document.RootElement.GetRawText();
    }

    private static ResumeDetailDto ToDetail(ResumeEntry entry)
    {
        var sections = entry.Sections
            .OrderBy(section => section.SortOrder)
            .ToList();
        var childrenByParent = sections.ToLookup(section => section.ParentSectionId);

        return new ResumeDetailDto(
            entry.Id,
            entry.Title,
            entry.SourceFileName,
            entry.CreatedAt,
            BuildSectionDtos(null, childrenByParent));
    }

    private static IReadOnlyList<ResumeSectionDto> BuildSectionDtos(int? parentId, ILookup<int?, ResumeSection> childrenByParent)
    {
        var children = childrenByParent[parentId].ToList();
        if (children.Count == 0)
        {
            return [];
        }

        return children
            .OrderBy(section => section.SortOrder)
            .Select(section => new ResumeSectionDto(
                section.Id,
                section.ResumeEntryId,
                section.ParentSectionId,
                section.HeadingLevel,
                section.Title,
                section.Content,
                section.SortOrder,
                BuildSectionDtos(section.Id, childrenByParent)))
            .ToArray();
    }

    private static string NormalizeTitle(string title, string? sourceFileName)
    {
        if (!string.IsNullOrWhiteSpace(title))
        {
            return title.Trim();
        }

        return string.IsNullOrWhiteSpace(sourceFileName)
            ? "Resume"
            : Path.GetFileNameWithoutExtension(sourceFileName);
    }

    private static byte[]? DecodeOptionalBase64(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : Convert.FromBase64String(value);
    }

    private static string ComputeHash(byte[] bytes)
    {
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private static string SanitizeFileName(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        var cleaned = new string(value.Select(ch => invalid.Contains(ch) ? '-' : ch).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(cleaned) ? "resume" : cleaned;
    }
}
