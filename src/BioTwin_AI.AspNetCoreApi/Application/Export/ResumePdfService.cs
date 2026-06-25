using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace BioTwin_AI.AspNetCoreApi.Application.Export;

public sealed class ResumePdfService : IResumePdfService
{
    public byte[] Generate(string markdown, string? title)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            throw new ArgumentException("Markdown content is required.", nameof(markdown));
        }

        var lines = markdown.Replace("\r\n", "\n").Split('\n');
        var documentTitle = string.IsNullOrWhiteSpace(title) ? "Resume" : title.Trim();

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(42);
                page.DefaultTextStyle(style => style.FontSize(10).FontColor(Colors.Grey.Darken3));
                page.Header().Text(documentTitle).SemiBold().FontSize(16).FontColor(Colors.Blue.Darken3);
                page.Content().PaddingTop(16).Column(column =>
                {
                    column.Spacing(5);
                    foreach (var line in lines)
                    {
                        column.Item().Text(CleanMarkdownLine(line)).FontSize(GetFontSize(line));
                    }
                });
                page.Footer().AlignCenter().Text(text =>
                {
                    text.CurrentPageNumber().FontSize(8);
                    text.Span(" / ").FontSize(8);
                    text.TotalPages().FontSize(8);
                });
            });
        }).GeneratePdf();
    }

    private static string CleanMarkdownLine(string line)
    {
        return line.Trim().TrimStart('#', '-', '*', ' ');
    }

    private static int GetFontSize(string line)
    {
        var trimmed = line.TrimStart();
        if (trimmed.StartsWith("# "))
        {
            return 18;
        }

        if (trimmed.StartsWith("## "))
        {
            return 14;
        }

        return 10;
    }
}
