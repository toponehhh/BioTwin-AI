using BioTwin_AI.AspNetCoreApi.Application.Embeddings;
using BioTwin_AI.AspNetCoreApi.Application.Llm;
using BioTwin_AI.AspNetCoreApi.Application.Profiles;
using BioTwin_AI.AspNetCoreApi.Application.Resumes;
using BioTwin_AI.AspNetCoreApi.Infrastructure.Data;
using BioTwin_AI.DotNetShared.Resumes;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using AiChatMessage = Microsoft.Extensions.AI.ChatMessage;
using AiChatOptions = Microsoft.Extensions.AI.ChatOptions;

namespace BioTwin_AI.AspNetCoreApi.Tests.Application;

public sealed class ResumeLanguageWorkspaceTests
{
    [Fact]
    public async Task SaveAsync_updates_existing_canonical_resume_for_the_same_language()
    {
        await using var context = CreateContext();
        var service = CreateService(context);

        var first = await service.SaveAsync(
            "huangd",
            new SaveResumeMarkdownRequest("中文简历", "# 中文简历\n\n## 2025\n旧内容", ResumeLanguages.SimplifiedChinese, null, null, null),
            userId: null,
            CancellationToken.None);
        var second = await service.SaveAsync(
            "huangd",
            new SaveResumeMarkdownRequest("中文简历新版", "# 中文简历新版\n\n## 2026\n新内容", ResumeLanguages.SimplifiedChinese, null, null, null),
            userId: null,
            CancellationToken.None);

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(1, await context.ResumeEntries.CountAsync());
        Assert.Equal(ResumeLanguages.SimplifiedChinese, second.Language);
        Assert.Contains(Flatten(second.Sections), section => section.Title.Contains("2026", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SaveAsync_allows_one_resume_per_supported_language()
    {
        await using var context = CreateContext();
        var service = CreateService(context);

        await service.SaveAsync(
            "huangd",
            new SaveResumeMarkdownRequest("中文简历", "# 中文简历", ResumeLanguages.SimplifiedChinese, null, null, null),
            userId: null,
            CancellationToken.None);
        await service.SaveAsync(
            "huangd",
            new SaveResumeMarkdownRequest("English Resume", "# English Resume", ResumeLanguages.English, null, null, null),
            userId: null,
            CancellationToken.None);

        var languages = await context.ResumeEntries
            .OrderBy(entry => entry.Language)
            .Select(entry => entry.Language)
            .ToArrayAsync();

        Assert.Equal([ResumeLanguages.English, ResumeLanguages.SimplifiedChinese], languages);
    }

    [Fact]
    public async Task MergePreviewAsync_returns_merged_markdown_without_writing_resume_entries()
    {
        await using var context = CreateContext();
        var service = CreateService(context);
        await service.SaveAsync(
            "huangd",
            new SaveResumeMarkdownRequest("Donald Huang", "# Donald Huang\n\n## 2025\nBuilt systems.", ResumeLanguages.English, null, null, null),
            userId: null,
            CancellationToken.None);
        var beforeCount = await context.ResumeEntries.CountAsync();

        var preview = await service.MergePreviewAsync(
            "huangd",
            new MergeResumeMarkdownRequest(
                ResumeLanguages.English,
                "Donald Huang Updated",
                "# Donald Huang\n\n## 2026\nLed AI migration.",
                "donald-2026.md"),
            CancellationToken.None);

        Assert.NotNull(preview);
        Assert.Equal(beforeCount, await context.ResumeEntries.CountAsync());
        Assert.Equal(ResumeLanguages.English, preview!.Language);
        Assert.Contains("Built systems", preview.MergedMarkdown, StringComparison.Ordinal);
        Assert.Contains("Led AI migration", preview.MergedMarkdown, StringComparison.Ordinal);
    }

    private static ResumeService CreateService(BioTwinApiDbContext context)
    {
        return new ResumeService(
            context,
            new FakeEmbeddingService(),
            new FakeHttpClientFactory(),
            new ConfigurationBuilder().Build(),
            NullLogger<ResumeService>.Instance,
            new FakeLlmChatService(),
            new CandidateProfileExtractionService(
                context,
                new CandidateProfileInfoService(context),
                new ProfileShareCodeGenerator()),
            new ResumeOperationService(
                new MemoryResumeOperationCoordinator(),
                new ResumeStateTokenService(context),
                TimeProvider.System));
    }

    private sealed class FakeEmbeddingService : IEmbeddingService
    {
        public Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken)
        {
            return Task.FromResult(new[] { 0.1f, 0.2f, 0.3f });
        }
    }

    private sealed class FakeHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            return new HttpClient();
        }
    }

    private sealed class FakeLlmChatService : ILlmChatService
    {
        public Task<string> CompleteAsync(IEnumerable<AiChatMessage> messages, AiChatOptions options, LlmRequestKind requestKind, CancellationToken cancellationToken)
        {
            return Task.FromResult("# Donald Huang\n\n## 2025\nBuilt systems.\n\n## 2026\nLed AI migration.");
        }

        public async IAsyncEnumerable<string> StreamAsync(IEnumerable<AiChatMessage> messages, AiChatOptions options, LlmRequestKind requestKind, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            yield break;
        }
    }

    private static IReadOnlyList<ResumeSectionDto> Flatten(IReadOnlyList<ResumeSectionDto> sections)
    {
        var result = new List<ResumeSectionDto>();
        foreach (var section in sections)
        {
            result.Add(section);
            result.AddRange(Flatten(section.Children));
        }

        return result;
    }

    private static BioTwinApiDbContext CreateContext()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<BioTwinApiDbContext>()
            .UseSqlite(connection)
            .Options;
        var context = new BioTwinApiDbContext(options);
        context.Database.EnsureCreated();
        return context;
    }
}
