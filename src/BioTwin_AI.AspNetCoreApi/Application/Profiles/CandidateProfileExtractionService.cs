using System.Text.Json;
using System.Text.RegularExpressions;
using BioTwin_AI.AspNetCoreApi.Infrastructure.Data;
using BioTwin_AI.DotNetShared.Profiles;
using Microsoft.EntityFrameworkCore;

namespace BioTwin_AI.AspNetCoreApi.Application.Profiles;

public sealed partial class CandidateProfileExtractionService(
    BioTwinApiDbContext dbContext,
    ICandidateProfileInfoService profileInfoService,
    IProfileShareCodeGenerator profileShareCodeGenerator) : ICandidateProfileExtractionService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task GenerateFromResumeAsync(int userId, string markdown, CancellationToken cancellationToken)
    {
        var user = await dbContext.UserAccounts.FirstOrDefaultAsync(candidate => candidate.Id == userId, cancellationToken);
        if (user is null)
        {
            return;
        }

        user.ProfileHash = await GenerateUniqueProfileHashAsync(cancellationToken);
        user.ProfileHashUpdatedAt = DateTimeOffset.UtcNow;
        user.CandidateProfileVersion++;
        user.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        var previousCareer = await profileInfoService.GetCurrentAsync(userId, "careertimeline", cancellationToken);
        var previousWork = await profileInfoService.GetCurrentAsync(userId, "worktimeline", cancellationToken);
        var sourceVersion = user.CandidateProfileVersion;

        await profileInfoService.CreateNextVersionAsync(
            userId,
            "careertimeline",
            JsonSerializer.Serialize(BuildCareerTimeline(markdown), JsonOptions),
            "llm_generated",
            sourceVersion,
            previousCareer?.Id,
            userId,
            cancellationToken);
        await profileInfoService.CreateNextVersionAsync(
            userId,
            "worktimeline",
            JsonSerializer.Serialize(BuildWorkTimeline(markdown), JsonOptions),
            "llm_generated",
            sourceVersion,
            previousWork?.Id,
            userId,
            cancellationToken);
    }

    private async Task<string> GenerateUniqueProfileHashAsync(CancellationToken cancellationToken)
    {
        var code = profileShareCodeGenerator.Generate();
        while (await dbContext.UserAccounts.IgnoreQueryFilters().AnyAsync(user => user.ProfileHash == code, cancellationToken))
        {
            code = profileShareCodeGenerator.Generate();
        }

        return code;
    }

    private static IReadOnlyList<CareerTimelineItemDto> BuildCareerTimeline(string markdown)
    {
        var headings = ExtractHeadings(markdown)
            .Where(heading => !IsWorkHeading(heading.Title))
            .Take(6)
            .ToArray();
        if (headings.Length == 0)
        {
            headings = [new MarkdownHeading("Current Focus", FirstParagraph(markdown))];
        }

        return headings.Select((heading, index) => new CareerTimelineItemDto(
                Title: heading.Title,
                Subtitle: index == 0 ? "Current experience" : "Experience",
                PeriodLabel: index == 0 ? "NOW" : DateTime.UtcNow.Year.ToString(),
                SortOrder: index,
                Description: string.IsNullOrWhiteSpace(heading.Description)
                    ? "Extracted from resume content."
                    : heading.Description,
                IsHighlighted: index == 0))
            .ToArray();
    }

    private static IReadOnlyList<WorkTimelineItemDto> BuildWorkTimeline(string markdown)
    {
        var headings = ExtractHeadings(markdown)
            .Where(heading => IsWorkHeading(heading.Title))
            .Take(6)
            .ToArray();
        if (headings.Length == 0)
        {
            headings = ExtractHeadings(markdown).Take(3).ToArray();
        }

        return headings.Select((heading, index) => new WorkTimelineItemDto(
                Number: (index + 1).ToString("00"),
                Title: heading.Title,
                Description: string.IsNullOrWhiteSpace(heading.Description)
                    ? "Extracted from resume content."
                    : heading.Description,
                ImageUrl: null,
                LinkUrl: null,
                SortOrder: index))
            .ToArray();
    }

    private static IReadOnlyList<MarkdownHeading> ExtractHeadings(string markdown)
    {
        var source = markdown ?? string.Empty;
        var matches = HeadingRegex().Matches(source);
        var result = new List<MarkdownHeading>();
        for (var i = 0; i < matches.Count; i++)
        {
            var current = matches[i];
            var title = current.Groups["title"].Value.Trim();
            var contentStart = current.Index + current.Length;
            var contentEnd = i + 1 < matches.Count ? matches[i + 1].Index : source.Length;
            var description = FirstParagraph(source[contentStart..contentEnd]);
            if (!string.IsNullOrWhiteSpace(title))
            {
                result.Add(new MarkdownHeading(title, description));
            }
        }

        return result;
    }

    private static bool IsWorkHeading(string title)
    {
        return title.Contains("project", StringComparison.OrdinalIgnoreCase)
            || title.Contains("work", StringComparison.OrdinalIgnoreCase)
            || title.Contains("portfolio", StringComparison.OrdinalIgnoreCase);
    }

    private static string FirstParagraph(string markdown)
    {
        var line = (markdown ?? string.Empty)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .FirstOrDefault(item => !item.StartsWith('#'));
        return line ?? string.Empty;
    }

    private sealed record MarkdownHeading(string Title, string Description);

    [GeneratedRegex(@"(?m)^#{1,6}\s+(?<title>.+)$")]
    private static partial Regex HeadingRegex();
}
