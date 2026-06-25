using System.Text.RegularExpressions;

namespace BioTwin_AI.AspNetCoreApi.Application.Resumes;

public static class ResumeMarkdownParser
{
    public static IReadOnlyList<ResumeMarkdownSection> Split(string markdown, string fallbackTitle)
    {
        var normalized = (markdown ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
        var fallback = string.IsNullOrWhiteSpace(fallbackTitle) ? "Resume" : fallbackTitle.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return [new ResumeMarkdownSection(fallback, string.Empty, 1, null)];
        }

        var lines = normalized.Split('\n');
        var headings = lines
            .Select((line, index) => new { Line = line, Index = index, Match = Regex.Match(line, @"^\s{0,3}(#{1,6})\s+(.+?)\s*#*\s*$") })
            .Where(item => item.Match.Success)
            .Select(item => new
            {
                item.Index,
                Level = item.Match.Groups[1].Value.Length,
                Title = Regex.Replace(item.Match.Groups[2].Value.Trim(), @"\s+", " ")
            })
            .Where(item => item.Title.Length > 0)
            .ToList();

        if (headings.Count == 0)
        {
            return [new ResumeMarkdownSection(fallback, normalized.Trim(), 1, null)];
        }

        var sections = new List<ResumeMarkdownSection>();
        var headingToSectionIndex = new Dictionary<int, int>();
        var ancestorByLevel = new Dictionary<int, int>();

        if (headings[0].Index > 0)
        {
            var intro = string.Join('\n', lines.Take(headings[0].Index)).Trim();
            if (intro.Length > 0)
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
                if (ancestorByLevel.TryGetValue(level, out var parentHeadingIndex) &&
                    headingToSectionIndex.TryGetValue(parentHeadingIndex, out var parentSectionIndex))
                {
                    parentIndex = parentSectionIndex;
                    break;
                }
            }

            var sectionIndex = sections.Count;
            sections.Add(new ResumeMarkdownSection(current.Title, body, Math.Clamp(current.Level, 1, 6), parentIndex));
            headingToSectionIndex[i] = sectionIndex;
            ancestorByLevel[current.Level] = i;

            foreach (var stale in ancestorByLevel.Keys.Where(level => level > current.Level).ToList())
            {
                ancestorByLevel.Remove(stale);
            }
        }

        return sections;
    }
}
