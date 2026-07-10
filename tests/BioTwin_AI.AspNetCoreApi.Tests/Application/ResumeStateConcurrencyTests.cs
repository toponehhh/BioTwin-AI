using BioTwin_AI.AspNetCoreApi.Application.Embeddings;
using BioTwin_AI.AspNetCoreApi.Application.Llm;
using BioTwin_AI.AspNetCoreApi.Application.Profiles;
using BioTwin_AI.AspNetCoreApi.Application.Resumes;
using BioTwin_AI.AspNetCoreApi.Infrastructure.Data;
using BioTwin_AI.AspNetCoreApi.Infrastructure.Data.Entities;
using BioTwin_AI.DotNetShared.Resumes;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace BioTwin_AI.AspNetCoreApi.Tests.Application;

public sealed class ResumeStateConcurrencyTests
{
    [Fact]
    public async Task SaveAsync_rejects_a_client_token_after_resume_state_changes()
    {
        await using var context = CreateContext();
        var tokenService = new ResumeStateTokenService(context);
        var operationService = new ResumeOperationService(
            new MemoryResumeOperationCoordinator(),
            tokenService,
            TimeProvider.System);
        var currentToken = await tokenService.ComputeAsync("huangd", CancellationToken.None);
        var lease = await operationService.AcquireAsync(7, "huangd", "import", currentToken, CancellationToken.None);
        context.ResumeEntries.Add(new ResumeEntry
        {
            TenantId = "huangd",
            Language = ResumeLanguages.English,
            Title = "Changed elsewhere",
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow
        });
        await context.SaveChangesAsync();
        var service = CreateService(context, operationService);
        var request = new SaveResumeMarkdownRequest(
            "Stale update",
            "# Stale update",
            ResumeLanguages.SimplifiedChinese,
            null,
            null,
            null,
            OperationId: lease.OperationId,
            ExpectedStateToken: currentToken);

        var exception = await Assert.ThrowsAsync<ResumeOperationConflictException>(() =>
            service.SaveAsync("huangd", request, 7, CancellationToken.None));

        Assert.Equal("resume_state_stale", exception.Code);
        Assert.Single(context.ResumeEntries);
    }

    private static ResumeService CreateService(BioTwinApiDbContext context, IResumeOperationService operationService)
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
            operationService);
    }

    private sealed class FakeEmbeddingService : IEmbeddingService
    {
        public Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken) =>
            Task.FromResult(new[] { 0.1f, 0.2f });
    }

    private sealed class FakeHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }

    private sealed class FakeLlmChatService : ILlmChatService
    {
        public Task<string> CompleteAsync(IEnumerable<ChatMessage> messages, ChatOptions options, LlmRequestKind requestKind, CancellationToken cancellationToken) =>
            Task.FromResult(string.Empty);

        public async IAsyncEnumerable<string> StreamAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions options,
            LlmRequestKind requestKind,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            yield break;
        }
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
