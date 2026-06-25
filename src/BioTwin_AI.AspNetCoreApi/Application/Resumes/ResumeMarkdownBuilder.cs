using System.Text;
using BioTwin_AI.AspNetCoreApi.Infrastructure.Data.Entities;

namespace BioTwin_AI.AspNetCoreApi.Application.Resumes;

public static class ResumeMarkdownBuilder
{
    public static string Build(IEnumerable<ResumeSection> sections, string? resumeTitle)
    {
        var ordered = sections.OrderBy(section => section.SortOrder).ToList();
        var builder = new StringBuilder();

        if (!ordered.Any(section => section.HeadingLevel == 1) && !string.IsNullOrWhiteSpace(resumeTitle))
        {
            builder.AppendLine($"# {resumeTitle.Trim()}");
            builder.AppendLine();
        }

        foreach (var section in ordered)
        {
            builder.AppendLine($"{new string('#', Math.Clamp(section.HeadingLevel, 1, 6))} {section.Title}");
            builder.AppendLine();
            builder.AppendLine(section.Content);
            builder.AppendLine();
        }

        return builder.ToString().Trim();
    }
}
