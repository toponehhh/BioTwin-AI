using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BioTwin_AI.AspNetCoreApi.Application.Embeddings;
using BioTwin_AI.AspNetCoreApi.Application.Llm;
using BioTwin_AI.AspNetCoreApi.Application.Profiles;
using BioTwin_AI.AspNetCoreApi.Infrastructure.Data;
using BioTwin_AI.AspNetCoreApi.Infrastructure.Data.Entities;
using BioTwin_AI.DotNetShared.Resumes;
using Microsoft.EntityFrameworkCore;
using AiChatMessage = Microsoft.Extensions.AI.ChatMessage;
using AiChatOptions = Microsoft.Extensions.AI.ChatOptions;
using AiChatRole = Microsoft.Extensions.AI.ChatRole;

namespace BioTwin_AI.AspNetCoreApi.Application.Resumes;

public sealed class ResumeService(
    BioTwinApiDbContext dbContext,
    IEmbeddingService embeddingService,
    IHttpClientFactory httpClientFactory,
    IConfiguration configuration,
    ILogger<ResumeService> logger,
    ILlmChatService llmChatService,
    ICandidateProfileExtractionService candidateProfileExtractionService) : IResumeService
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
            .OrderBy(entry => entry.Language)
            .ThenByDescending(entry => entry.CreatedAt)
            .Select(entry => new ResumeSummaryDto(
                entry.Id,
                entry.Title,
                entry.Language,
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
                duplicate.Language,
                IsDuplicate: true,
                duplicate.Id,
                duplicate.Title);
        }

        var markdown = await ConvertBytesToMarkdownAsync(file.FileName, file.ContentType, bytes, cancellationToken);
        var detectedLanguage = DetectLanguage(markdown);
        return new ConvertedResumeFileDto(
            Path.GetFileNameWithoutExtension(file.FileName),
            file.FileName,
            markdown,
            detectedLanguage,
            IsDuplicate: false,
            ExistingResumeEntryId: null,
            ExistingResumeTitle: null);
    }

    public async Task<ResumeDetailDto> SaveAsync(string tenantId, SaveResumeMarkdownRequest request, int? userId, CancellationToken cancellationToken)
    {
        EnsureMarkdownCanBeSaved(request.Markdown);
        var language = NormalizeRequiredLanguage(request.Language);
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

        var existing = await dbContext.ResumeEntries
            .Include(entry => entry.Sections)
            .ThenInclude(section => section.Vector)
            .FirstOrDefaultAsync(entry => entry.TenantId == tenantId && entry.Language == language, cancellationToken);

        if (existing is not null)
        {
            return await ReplaceExistingMarkdownAsync(existing, request, language, sourceBytes, userId, cancellationToken);
        }

        var now = DateTimeOffset.UtcNow;
        var entry = new ResumeEntry
        {
            TenantId = tenantId,
            Language = language,
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
        if (userId is > 0)
        {
            await candidateProfileExtractionService.GenerateFromResumeAsync(userId.Value, request.Markdown, cancellationToken);
        }

        return ToDetail(entry);
    }

    public async Task<ResumeDetailDto?> ReplaceMarkdownAsync(string tenantId, int resumeId, SaveResumeMarkdownRequest request, int? userId, CancellationToken cancellationToken)
    {
        EnsureMarkdownCanBeSaved(request.Markdown);
        var language = NormalizeRequiredLanguage(request.Language);
        var entry = await dbContext.ResumeEntries
            .Include(resume => resume.Sections)
            .ThenInclude(section => section.Vector)
            .FirstOrDefaultAsync(resume => resume.TenantId == tenantId && resume.Id == resumeId, cancellationToken);

        if (entry is null)
        {
            return null;
        }

        if (!string.Equals(entry.Language, language, StringComparison.OrdinalIgnoreCase)
            && await dbContext.ResumeEntries.AnyAsync(resume => resume.TenantId == tenantId && resume.Language == language && resume.Id != resumeId, cancellationToken))
        {
            throw new InvalidOperationException($"A canonical resume already exists for language '{language}'.");
        }

        var sourceBytes = DecodeOptionalBase64(request.SourceFileContentBase64);
        return await ReplaceExistingMarkdownAsync(entry, request, language, sourceBytes, userId, cancellationToken);
    }

    public async Task<MergeResumeMarkdownResponse?> MergePreviewAsync(string tenantId, MergeResumeMarkdownRequest request, CancellationToken cancellationToken)
    {
        var language = NormalizeRequiredLanguage(request.Language);
        if (string.IsNullOrWhiteSpace(request.DraftMarkdown))
        {
            throw new InvalidOperationException("Draft Markdown cannot be empty.");
        }

        var canonical = await dbContext.ResumeEntries
            .AsNoTracking()
            .Include(entry => entry.Sections)
            .FirstOrDefaultAsync(entry => entry.TenantId == tenantId && entry.Language == language, cancellationToken);
        if (canonical is null)
        {
            return null;
        }

        var canonicalMarkdown = ResumeMarkdownBuilder.Build(canonical.Sections, canonical.Title);
        var warnings = new List<string>();
        var mergedMarkdown = await MergeMarkdownWithLlmAsync(
            canonical.Title,
            language,
            canonicalMarkdown,
            request.DraftTitle,
            request.DraftMarkdown,
            cancellationToken);
        if (string.IsNullOrWhiteSpace(mergedMarkdown))
        {
            warnings.Add("LLM merge returned empty output; generated a deterministic review merge.");
            mergedMarkdown = MergeMarkdownByReviewBlock(canonicalMarkdown, request.DraftMarkdown, request.SourceFileName);
        }

        return new MergeResumeMarkdownResponse(
            language,
            canonical.Id,
            string.IsNullOrWhiteSpace(canonical.Title) ? NormalizeTitle(request.DraftTitle, request.SourceFileName) : canonical.Title,
            mergedMarkdown,
            warnings);
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

    private async Task<ResumeDetailDto> ReplaceExistingMarkdownAsync(
        ResumeEntry entry,
        SaveResumeMarkdownRequest request,
        string language,
        byte[]? sourceBytes,
        int? userId,
        CancellationToken cancellationToken)
    {
        entry.Language = language;
        entry.Title = NormalizeTitle(request.Title, entry.SourceFileName);
        entry.SourceFileName = request.SourceFileName ?? entry.SourceFileName;
        entry.SourceContentType = request.SourceContentType ?? entry.SourceContentType;
        entry.SourceFileSize = request.SourceFileSize ?? entry.SourceFileSize;
        entry.UpdatedAt = DateTimeOffset.UtcNow;

        if (sourceBytes is not null)
        {
            entry.SourceFileContent = sourceBytes;
            entry.SourceFileHash = ComputeHash(sourceBytes);
        }

        await ReplaceSectionsAsync(entry, request.Markdown, cancellationToken);
        if (userId is > 0)
        {
            await candidateProfileExtractionService.GenerateFromResumeAsync(userId.Value, request.Markdown, cancellationToken);
        }

        return ToDetail(entry);
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
            entry.Language,
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

    private static void EnsureMarkdownCanBeSaved(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            throw new InvalidOperationException("Markdown cannot be empty.");
        }
    }

    private static string NormalizeRequiredLanguage(string language)
    {
        if (!ResumeLanguages.IsSupported(language))
        {
            throw new InvalidOperationException($"Unsupported resume language '{language}'.");
        }

        return ResumeLanguages.Normalize(language);
    }

    private static string DetectLanguage(string markdown)
    {
        var cjk = 0;
        var latin = 0;
        foreach (var ch in markdown)
        {
            if ((ch >= '\u4e00' && ch <= '\u9fff') || (ch >= '\u3400' && ch <= '\u4dbf'))
            {
                cjk++;
            }
            else if ((ch >= 'a' && ch <= 'z') || (ch >= 'A' && ch <= 'Z'))
            {
                latin++;
            }
        }

        return cjk > 0 && cjk >= latin
            ? ResumeLanguages.SimplifiedChinese
            : latin > 0
                ? ResumeLanguages.English
                : ResumeLanguages.SimplifiedChinese;
    }

    private async Task<string> MergeMarkdownWithLlmAsync(
        string canonicalTitle,
        string language,
        string canonicalMarkdown,
        string draftTitle,
        string draftMarkdown,
        CancellationToken cancellationToken)
    {
        try
        {
            var response = await llmChatService.CompleteAsync(
                BuildMergeMessages(canonicalTitle, language, canonicalMarkdown, draftTitle, draftMarkdown),
                CreateMergeChatOptions(),
                cancellationToken);
            return NormalizeMarkdownResponse(response);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "LLM resume merge failed; falling back to deterministic review merge.");
            return string.Empty;
        }
    }

    private static IReadOnlyList<AiChatMessage> BuildMergeMessages(
        string canonicalTitle,
        string language,
        string canonicalMarkdown,
        string draftTitle,
        string draftMarkdown)
    {
        var languageInstruction = string.Equals(language, ResumeLanguages.English, StringComparison.OrdinalIgnoreCase)
            ? "Write the merged resume in English."
            : "Write the merged resume in Simplified Chinese.";
        var systemPrompt = $"""
You are a senior resume editor.
Merge two Markdown resumes for the same candidate into one canonical Markdown resume.
{languageInstruction}
Preserve factual details and do not invent employers, dates, technologies, awards, or education.
Merge experiences primarily by timeline, keep the most complete version of duplicate events, and remove obvious duplicates.
Keep clear Markdown heading structure.
Return Markdown only, without code fences or commentary.
""";

        var userPrompt = $"""
Canonical resume title:
{canonicalTitle}

Canonical resume Markdown:
{canonicalMarkdown}

Imported draft title:
{draftTitle}

Imported draft Markdown:
{draftMarkdown}
""";

        return
        [
            new AiChatMessage(AiChatRole.System, systemPrompt),
            new AiChatMessage(AiChatRole.User, userPrompt)
        ];
    }

    private AiChatOptions CreateMergeChatOptions()
    {
        return new AiChatOptions
        {
            ModelId = configuration["LLM:Model"] ?? "openrouter/free",
            Temperature = (float)configuration.GetValue("LLM:MergeTemperature", 0.1),
            MaxOutputTokens = configuration.GetValue("LLM:MergeMaxTokens", 5000)
        };
    }

    private static string NormalizeMarkdownResponse(string? response)
    {
        var markdown = (response ?? string.Empty)
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Trim();
        if (markdown.StartsWith("```markdown", StringComparison.OrdinalIgnoreCase))
        {
            markdown = markdown[11..].Trim();
        }
        else if (markdown.StartsWith("```", StringComparison.Ordinal))
        {
            markdown = markdown[3..].Trim();
        }

        if (markdown.EndsWith("```", StringComparison.Ordinal))
        {
            markdown = markdown[..^3].Trim();
        }

        return markdown;
    }

    private static string MergeMarkdownByReviewBlock(string canonicalMarkdown, string draftMarkdown, string? sourceFileName)
    {
        var sourceLabel = string.IsNullOrWhiteSpace(sourceFileName)
            ? "imported draft"
            : sourceFileName.Trim();
        return string.Join(
            "\n\n",
            canonicalMarkdown.Trim(),
            "---",
            $"## Imported update for review: {sourceLabel}",
            draftMarkdown.Trim());
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
