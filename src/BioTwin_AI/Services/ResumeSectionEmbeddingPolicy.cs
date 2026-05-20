using BioTwin_AI.Models;

namespace BioTwin_AI.Services;

public static class ResumeSectionEmbeddingPolicy
{
    private static readonly char[] SeparatorChars = ['-', '*', '_', '=', '—', '–', '─'];

    public static bool TryCreateChunk(
        ResumeSection section,
        string sourceFileName,
        IReadOnlyList<string> titleBreadcrumb,
        out ResumeSectionChunk? chunk)
    {
        chunk = null;

        var sanitizedContent = RemoveSeparatorLines(section.Content);
        var sectionTitle = section.Title?.Trim() ?? string.Empty;
        if (!HasMeaningfulText(sectionTitle) && !HasMeaningfulText(sanitizedContent))
        {
            return false;
        }

        chunk = new ResumeSectionChunk
        {
            Chunk = sanitizedContent,
            Metadata = new ResumeSectionChunkMetadata
            {
                SectionTitle = section.Title ?? string.Empty,
                HeadingLevel = section.HeadingLevel,
                TitleBreadcrumb = titleBreadcrumb,
                ResumeSectionId = section.Id,
                ResumeEntryId = section.ResumeEntryId,
                SourceFile = sourceFileName,
                TenantId = section.TenantId
            }
        };

        return true;
    }

    private static string RemoveSeparatorLines(string? content)
    {
        if (string.IsNullOrWhiteSpace(content))
        {
            return string.Empty;
        }

        var meaningfulLines = content
            .Replace("\r\n", "\n")
            .Replace('\r', '\n')
            .Split('\n')
            .Where(line => !IsSeparatorLine(line))
            .ToList();

        return string.Join('\n', meaningfulLines).Trim();
    }

    private static bool HasMeaningfulText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var withoutSeparators = RemoveSeparatorLines(value);
        return withoutSeparators.Any(char.IsLetterOrDigit);
    }

    private static bool IsSeparatorLine(string line)
    {
        var compact = new string(line.Where(character => !char.IsWhiteSpace(character)).ToArray());
        return compact.Length >= 3 && compact.All(character => SeparatorChars.Contains(character));
    }
}
