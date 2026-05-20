using System.Text;
using BioTwin_AI.Models;
using Markdig;
using Markdig.Syntax;

namespace BioTwin_AI.Services;

public static class ResumeExportMarkdownBuilder
{
    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .Build();

    public static string BuildMarkdown(IEnumerable<ResumeSection> sections, string? resumeTitle)
    {
        var orderedSections = sections
            .OrderBy(section => section.SortOrder)
            .ToList();

        var sb = new StringBuilder();
        if (!orderedSections.Any(section => Math.Clamp(section.HeadingLevel, 1, 6) == 1) &&
            !string.IsNullOrWhiteSpace(resumeTitle))
        {
            sb.AppendLine($"# {resumeTitle.Trim()}");
            sb.AppendLine();
        }

        foreach (var item in orderedSections)
        {
            sb.AppendLine($"{new string('#', Math.Clamp(item.HeadingLevel, 1, 6))} {item.Title}");
            sb.AppendLine();
            sb.AppendLine(item.Content);
            sb.AppendLine();
        }

        return sb.ToString().Trim();
    }

    public static string? GetPdfHeaderTitle(string markdown, string? resumeTitle)
    {
        if (ContainsLevelOneHeading(markdown) || string.IsNullOrWhiteSpace(resumeTitle))
        {
            return null;
        }

        return resumeTitle.Trim();
    }

    private static bool ContainsLevelOneHeading(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            return false;
        }

        return Markdown.Parse(markdown, Pipeline)
            .OfType<HeadingBlock>()
            .Any(heading => heading.Level == 1);
    }
}
