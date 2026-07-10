using BioTwin_AI.AspNetCoreApi.Application.Embeddings;
using BioTwin_AI.AspNetCoreApi.Application.Llm;
using BioTwin_AI.AspNetCoreApi.Application.Profiles;
using BioTwin_AI.AspNetCoreApi.Application.Resumes;
using BioTwin_AI.AspNetCoreApi.Infrastructure.Data;
using BioTwin_AI.AspNetCoreApi.Infrastructure.Data.Entities;
using BioTwin_AI.DotNetShared.Resumes;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using AiChatMessage = Microsoft.Extensions.AI.ChatMessage;
using AiChatOptions = Microsoft.Extensions.AI.ChatOptions;

namespace BioTwin_AI.AspNetCoreApi.Tests.Application;

public sealed class ResumeProfileIntegrationTests
{
    [Fact]
    public async Task SaveAsync_for_authenticated_candidate_generates_profile_infos_and_rotates_hash()
    {
        await using var context = CreateContext();
        var user = new UserAccount
        {
            Username = "candidate",
            Nickname = "Candidate",
            PasswordHash = "hash",
            ProfileHash = "OLDHASH8"
        };
        context.UserAccounts.Add(user);
        await context.SaveChangesAsync();
        var extractionService = new CandidateProfileExtractionService(
            context,
            new CandidateProfileInfoService(context),
            new SequenceShareCodeGenerator("NEWHASH8"));
        var stateTokenService = new ResumeStateTokenService(context);
        var operationService = new ResumeOperationService(
            new MemoryResumeOperationCoordinator(),
            stateTokenService,
            TimeProvider.System);
        var stateToken = await stateTokenService.ComputeAsync("candidate", CancellationToken.None);
        var lease = await operationService.AcquireAsync(user.Id, "candidate", "create", stateToken, CancellationToken.None);
        var resumeService = new ResumeService(
            context,
            new FakeEmbeddingService(),
            new FakeHttpClientFactory(),
            new ConfigurationBuilder().Build(),
            NullLogger<ResumeService>.Instance,
            new FakeLlmChatService(),
            extractionService,
            operationService);

        await resumeService.SaveAsync(
            "candidate",
            new SaveResumeMarkdownRequest(
                "Candidate Resume",
                "## AI Engineer\nBuilt local AI systems.",
                ResumeLanguages.English,
                null,
                null,
                null,
                OperationId: lease.OperationId,
                ExpectedStateToken: stateToken),
            user.Id,
            CancellationToken.None);

        var reloadedUser = await context.UserAccounts.SingleAsync();
        Assert.Equal("NEWHASH8", reloadedUser.ProfileHash);
        Assert.Contains(context.CandidateProfileInfos, info => info.UserId == user.Id && info.InfoType == "careertimeline");
        Assert.Contains(context.CandidateProfileInfos, info => info.UserId == user.Id && info.InfoType == "worktimeline");
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
            return Task.FromResult(string.Empty);
        }

        public async IAsyncEnumerable<string> StreamAsync(IEnumerable<AiChatMessage> messages, AiChatOptions options, LlmRequestKind requestKind, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            await Task.CompletedTask;
            yield break;
        }
    }

    private sealed class SequenceShareCodeGenerator(params string[] codes) : IProfileShareCodeGenerator
    {
        private int index;

        public string Generate(int length = ProfileShareCodeGenerator.DefaultLength)
        {
            return codes[Math.Min(index++, codes.Length - 1)];
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
