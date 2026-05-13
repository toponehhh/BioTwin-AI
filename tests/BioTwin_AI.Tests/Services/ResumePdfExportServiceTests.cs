using BioTwin_AI.Services;
using QuestPDF.Infrastructure;
using Xunit;

namespace BioTwin_AI.Tests.Services;

public class ResumePdfExportServiceTests
{
    public ResumePdfExportServiceTests()
    {
        QuestPDF.Settings.License = LicenseType.Community;
    }

    [Fact]
    public void GeneratePdf_WithMarkdown_ReturnsPdfBytes()
    {
        var service = new ResumePdfExportService();

        var bytes = service.GeneratePdf(
            """
            # Jane Candidate

            ## Experience

            - Built RAG workflows
            - Shipped Blazor applications

            ## Skills

            | Area | Skill |
            | --- | --- |
            | Backend | .NET |
            | AI | RAG |
            """,
            "Jane Candidate");

        Assert.True(bytes.Length > 0);
        Assert.Equal("%PDF"u8.ToArray(), bytes[..4]);
    }

    [Fact]
    public void GeneratePdf_WithEmptyMarkdown_Throws()
    {
        var service = new ResumePdfExportService();

        Assert.Throws<ArgumentException>(() => service.GeneratePdf(" ", "Resume"));
    }
}
