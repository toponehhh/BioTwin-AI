using BioTwin_AI.Models;
using BioTwin_AI.Services;
using Xunit;

namespace BioTwin_AI.Tests.Services;

public class ResumeExportMarkdownBuilderTests
{
    [Fact]
    public void BuildMarkdown_WithExistingH1_DoesNotPrependResumeTitle()
    {
        var sections = new[]
        {
            new ResumeSection
            {
                Title = "Jane Candidate",
                Content = "jane@example.com",
                HeadingLevel = 1,
                SortOrder = 0
            },
            new ResumeSection
            {
                Title = "Experience",
                Content = "- Built RAG workflows",
                HeadingLevel = 2,
                SortOrder = 1
            }
        };

        var markdown = ResumeExportMarkdownBuilder.BuildMarkdown(sections, "Imported Resume");

        Assert.StartsWith("# Jane Candidate", markdown);
        Assert.DoesNotContain("# Imported Resume", markdown);
    }

    [Fact]
    public void BuildMarkdown_WithoutExistingH1_PrependsResumeTitle()
    {
        var sections = new[]
        {
            new ResumeSection
            {
                Title = "Experience",
                Content = "- Built RAG workflows",
                HeadingLevel = 2,
                SortOrder = 0
            }
        };

        var markdown = ResumeExportMarkdownBuilder.BuildMarkdown(sections, "Imported Resume");

        Assert.StartsWith("# Imported Resume", markdown);
        Assert.Contains("## Experience", markdown);
    }

    [Fact]
    public void GetPdfHeaderTitle_WithExistingH1_ReturnsNull()
    {
        var headerTitle = ResumeExportMarkdownBuilder.GetPdfHeaderTitle(
            """
            # Jane Candidate

            ## Experience
            """,
            "Imported Resume");

        Assert.Null(headerTitle);
    }

    [Fact]
    public void GetPdfHeaderTitle_WithoutExistingH1_ReturnsResumeTitle()
    {
        var headerTitle = ResumeExportMarkdownBuilder.GetPdfHeaderTitle(
            """
            ## Experience

            - Built RAG workflows
            """,
            "Imported Resume");

        Assert.Equal("Imported Resume", headerTitle);
    }
}
