using System.Text;
using Markdig;
using Markdig.Extensions.Tables;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Microsoft.Extensions.Localization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace BioTwin_AI.Services;

public sealed class ResumePdfExportService
{
    private readonly IStringLocalizer<SharedResource>? _localizer;

    private static readonly MarkdownPipeline Pipeline = new MarkdownPipelineBuilder()
        .UseAdvancedExtensions()
        .Build();

    public ResumePdfExportService()
        : this(null)
    {
    }

    public ResumePdfExportService(IStringLocalizer<SharedResource>? localizer)
    {
        _localizer = localizer;
    }

    public byte[] GeneratePdf(string markdown, string title)
    {
        if (string.IsNullOrWhiteSpace(markdown))
        {
            throw new ArgumentException(T("MarkdownContentRequired", "Markdown content is required."), nameof(markdown));
        }

        var documentTitle = string.IsNullOrWhiteSpace(title) ? T("DefaultResumeTitle", "Resume") : title.Trim();
        var pageLabel = T("PdfPageLabel", "Page ");
        var pageOfLabel = T("PdfPageOfLabel", " of ");
        var markdownDocument = Markdown.Parse(markdown, Pipeline);

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(42);
                page.PageColor(Colors.White);
                page.DefaultTextStyle(style => style.FontSize(10).FontColor(Colors.Grey.Darken3));

                page.Header()
                    .PaddingBottom(10)
                    .BorderBottom(1)
                    .BorderColor(Colors.Grey.Lighten2)
                    .Text(documentTitle)
                    .SemiBold()
                    .FontSize(12)
                    .FontColor(Colors.Blue.Darken3);

                page.Content()
                    .PaddingVertical(16)
                    .Column(column =>
                    {
                        column.Spacing(8);

                        foreach (var block in markdownDocument)
                        {
                            column.Item().Element(container => ComposeBlock(container, block));
                        }
                    });

                page.Footer()
                    .AlignCenter()
                    .Text(text =>
                    {
                        text.Span(pageLabel).FontSize(8).FontColor(Colors.Grey.Medium);
                        text.CurrentPageNumber().FontSize(8).FontColor(Colors.Grey.Medium);
                        text.Span(pageOfLabel).FontSize(8).FontColor(Colors.Grey.Medium);
                        text.TotalPages().FontSize(8).FontColor(Colors.Grey.Medium);
                    });
            });
        }).GeneratePdf();
    }

    private static void ComposeBlock(IContainer container, Block block)
    {
        switch (block)
        {
            case HeadingBlock heading:
                ComposeHeading(container, heading);
                break;
            case ParagraphBlock paragraph:
                ComposeParagraph(container, paragraph);
                break;
            case ListBlock list:
                ComposeList(container, list);
                break;
            case CodeBlock code:
                ComposeCodeBlock(container, code);
                break;
            case QuoteBlock quote:
                ComposeQuote(container, quote);
                break;
            case ThematicBreakBlock:
                container.PaddingVertical(6).LineHorizontal(1).LineColor(Colors.Grey.Lighten2);
                break;
            case Table table:
                ComposeTable(container, table);
                break;
            case ContainerBlock containerBlock:
                ComposeContainerBlock(container, containerBlock);
                break;
            default:
                ComposeFallback(container, block);
                break;
        }
    }

    private static void ComposeHeading(IContainer container, HeadingBlock heading)
    {
        var text = GetInlineText(heading.Inline).Trim();
        var level = Math.Clamp(heading.Level, 1, 6);
        var fontSize = level switch
        {
            1 => 22,
            2 => 17,
            3 => 14,
            4 => 12,
            _ => 11
        };

        var headingContainer = level == 1
            ? container.PaddingTop(2).PaddingBottom(8).BorderBottom(1).BorderColor(Colors.Grey.Lighten2)
            : container.PaddingTop(6).PaddingBottom(2);

        headingContainer
            .Text(text)
            .SemiBold()
            .FontSize(fontSize)
            .FontColor(level == 1 ? Colors.Blue.Darken4 : Colors.Grey.Darken4);
    }

    private static void ComposeParagraph(IContainer container, ParagraphBlock paragraph)
    {
        var text = GetInlineText(paragraph.Inline).Trim();

        if (string.IsNullOrWhiteSpace(text))
        {
            container.Height(4);
            return;
        }

        container.Text(text).FontSize(10).FontColor(Colors.Grey.Darken3);
    }

    private static void ComposeList(IContainer container, ListBlock list)
    {
        var start = 1;
        _ = int.TryParse(list.OrderedStart, out start);
        if (start <= 0)
        {
            start = 1;
        }

        container.PaddingLeft(8).Column(column =>
        {
            column.Spacing(5);
            var index = start;

            foreach (var item in list.OfType<ListItemBlock>())
            {
                var marker = list.IsOrdered ? $"{index}." : "•";
                column.Item().Row(row =>
                {
                    row.ConstantItem(24).Text(marker).FontSize(10).FontColor(Colors.Grey.Darken2);
                    row.RelativeItem().Element(itemContainer => ComposeContainerBlock(itemContainer, item));
                });
                index++;
            }
        });
    }

    private static void ComposeCodeBlock(IContainer container, CodeBlock code)
    {
        var text = code.Lines.ToString().TrimEnd();

        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        container
            .Background(Colors.Grey.Lighten4)
            .Border(1)
            .BorderColor(Colors.Grey.Lighten2)
            .Padding(8)
            .Text(text)
            .FontSize(9)
            .FontColor(Colors.Grey.Darken4);
    }

    private static void ComposeQuote(IContainer container, QuoteBlock quote)
    {
        container
            .BorderLeft(3)
            .BorderColor(Colors.Blue.Lighten2)
            .PaddingLeft(10)
            .Column(column =>
            {
                column.Spacing(5);

                foreach (var block in quote)
                {
                    column.Item().Element(item => ComposeBlock(item, block));
                }
            });
    }

    private static void ComposeTable(IContainer container, Table table)
    {
        var rows = table.OfType<TableRow>().ToList();
        if (rows.Count == 0)
        {
            return;
        }

        var columnCount = rows.Max(row => row.OfType<TableCell>().Count());
        if (columnCount == 0)
        {
            return;
        }

        container.Table(tableDescriptor =>
        {
            tableDescriptor.ColumnsDefinition(columns =>
            {
                for (var i = 0; i < columnCount; i++)
                {
                    columns.RelativeColumn();
                }
            });

            for (var rowIndex = 0; rowIndex < rows.Count; rowIndex++)
            {
                var isHeader = rowIndex == 0;
                foreach (var cell in rows[rowIndex].OfType<TableCell>())
                {
                    var text = GetBlockText(cell).Trim();
                    var cellText = tableDescriptor.Cell()
                        .Border(1)
                        .BorderColor(Colors.Grey.Lighten2)
                        .Background(isHeader ? Colors.Grey.Lighten4 : Colors.White)
                        .Padding(5)
                        .Text(text)
                        .FontSize(9)
                        .FontColor(Colors.Grey.Darken3);

                    if (isHeader)
                    {
                        cellText.SemiBold();
                    }
                }
            }
        });
    }

    private static void ComposeContainerBlock(IContainer container, ContainerBlock containerBlock)
    {
        container.Column(column =>
        {
            column.Spacing(5);

            foreach (var child in containerBlock)
            {
                column.Item().Element(item => ComposeBlock(item, child));
            }
        });
    }

    private static void ComposeFallback(IContainer container, Block block)
    {
        var text = GetBlockText(block).Trim();

        if (!string.IsNullOrWhiteSpace(text))
        {
            container.Text(text).FontSize(10).FontColor(Colors.Grey.Darken3);
        }
    }

    private static string GetBlockText(Block block)
    {
        switch (block)
        {
            case LeafBlock leaf:
                return leaf.Inline is null ? leaf.Lines.ToString() : GetInlineText(leaf.Inline);
            case ContainerBlock container:
                var builder = new StringBuilder();

                foreach (var child in container)
                {
                    var childText = GetBlockText(child).Trim();
                    if (childText.Length > 0)
                    {
                        if (builder.Length > 0)
                        {
                            builder.AppendLine();
                        }

                        builder.Append(childText);
                    }
                }

                return builder.ToString();
            default:
                return block.ToString() ?? string.Empty;
        }
    }

    private static string GetInlineText(ContainerInline? inline)
    {
        if (inline is null)
        {
            return string.Empty;
        }

        var builder = new StringBuilder();

        for (var child = inline.FirstChild; child is not null; child = child.NextSibling)
        {
            AppendInlineText(builder, child);
        }

        return builder.ToString();
    }

    private static void AppendInlineText(StringBuilder builder, Inline inline)
    {
        switch (inline)
        {
            case LiteralInline literal:
                builder.Append(literal.Content);
                break;
            case CodeInline code:
                builder.Append(code.Content);
                break;
            case LineBreakInline:
                builder.AppendLine();
                break;
            case LinkInline link:
                AppendContainerInlineText(builder, link);
                if (link.IsImage)
                {
                    break;
                }

                if (!string.IsNullOrWhiteSpace(link.Url))
                {
                    builder.Append(" (").Append(link.Url).Append(')');
                }
                break;
            case AutolinkInline autoLink:
                builder.Append(autoLink.Url);
                break;
            case HtmlInline html:
                builder.Append(html.Tag);
                break;
            case ContainerInline container:
                AppendContainerInlineText(builder, container);
                break;
        }
    }

    private static void AppendContainerInlineText(StringBuilder builder, ContainerInline inline)
    {
        for (var child = inline.FirstChild; child is not null; child = child.NextSibling)
        {
            AppendInlineText(builder, child);
        }
    }

    private string T(string key, string fallback)
    {
        return _localizer?[key].Value ?? fallback;
    }
}
