using BioTwin_AI.Models;
using BioTwin_AI.Services;
using Xunit;

namespace BioTwin_AI.Tests.Services;

public class ResumeSectionEmbeddingPolicyTests
{
    [Fact]
    public void TryCreateChunk_WithOnlySeparators_ReturnsFalse()
    {
        var section = new ResumeSection
        {
            Id = 12,
            ResumeEntryId = 3,
            TenantId = "candidate1",
            Title = "---",
            Content = """
            ---
            ***
            ___
            """
        };

        var created = ResumeSectionEmbeddingPolicy.TryCreateChunk(
            section,
            "resume.md",
            new[] { "---" },
            out var chunk);

        Assert.False(created);
        Assert.Null(chunk);
    }

    [Fact]
    public void TryCreateChunk_WithMeaningfulContent_RemovesSeparatorLines()
    {
        var section = new ResumeSection
        {
            Id = 12,
            ResumeEntryId = 3,
            TenantId = "candidate1",
            Title = "Experience",
            Content = """
            Built RAG workflows
            ---
            Shipped Blazor applications
            """
        };

        var created = ResumeSectionEmbeddingPolicy.TryCreateChunk(
            section,
            "resume.md",
            new[] { "Experience" },
            out var chunk);

        Assert.True(created);
        Assert.NotNull(chunk);
        Assert.Equal(
            """
            Built RAG workflows
            Shipped Blazor applications
            """,
            chunk.Chunk);
    }
}
