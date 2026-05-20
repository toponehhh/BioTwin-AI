using BioTwin_AI.Data;
using BioTwin_AI.Models;
using BioTwin_AI.Services;
using BioTwin_AI.Tests.Fixtures;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace BioTwin_AI.Tests.Services;

public class ResumeUploadServiceDeleteTests
{
    [Fact]
    public async Task DeleteEntryAsync_RemovesResumeSectionsAndVectorsForCurrentTenantOnly()
    {
        var dbContext = DbContextFactory.CreateInMemoryContext();
        var session = new CurrentUserSession();
        session.SignIn("candidate1");
        var service = CreateService(dbContext, session);

        var target = CreateResume("candidate1", "delete-me");
        var otherTenant = CreateResume("candidate2", "keep-me");
        dbContext.ResumeEntries.AddRange(target, otherTenant);
        await dbContext.SaveChangesAsync();

        var deleted = await service.DeleteEntryAsync(target.Id);

        Assert.True(deleted);
        Assert.DoesNotContain(dbContext.ResumeEntries, entry => entry.Id == target.Id);
        Assert.DoesNotContain(dbContext.ResumeSections, section => section.ResumeEntryId == target.Id);
        Assert.DoesNotContain(dbContext.ResumeSectionVectors, vector => vector.ResumeSectionId == target.Sections[0].Id);
        Assert.Contains(dbContext.ResumeEntries, entry => entry.Id == otherTenant.Id);
        Assert.Contains(dbContext.ResumeSections, section => section.ResumeEntryId == otherTenant.Id);
    }

    private static ResumeUploadService CreateService(BioTwinDbContext dbContext, CurrentUserSession session)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                { "Rag:EmbeddingSize", "768" },
                { "ResumeMarkdownRefinement:Enabled", "false" }
            })
            .Build();
        var localizer = new PassThroughLocalizer();
        var refinementService = new ResumeMarkdownRefinementService(
            new FakeChatClient(),
            Mock.Of<ILogger<ResumeMarkdownRefinementService>>(),
            localizer,
            config);

        return new ResumeUploadService(
            dbContext,
            Mock.Of<IRagService>(),
            refinementService,
            new HttpClient(),
            Mock.Of<ILogger<ResumeUploadService>>(),
            session,
            localizer,
            config);
    }

    private static ResumeEntry CreateResume(string tenantId, string title)
    {
        var section = new ResumeSection
        {
            TenantId = tenantId,
            Title = "Experience",
            Content = "Built systems",
            SortOrder = 0,
            Vector = new ResumeSectionVector
            {
                TenantId = tenantId,
                SectionTitle = "Experience",
                Content = "Built systems",
                EmbeddingPayload = "[1]"
            }
        };

        return new ResumeEntry
        {
            TenantId = tenantId,
            Title = title,
            SourceFileName = $"{title}.md",
            Sections = { section }
        };
    }

    private sealed class PassThroughLocalizer : IStringLocalizer<SharedResource>
    {
        public LocalizedString this[string name] => new(name, name);

        public LocalizedString this[string name, params object[] arguments] =>
            new(name, string.Format(name, arguments));

        public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures)
        {
            return Array.Empty<LocalizedString>();
        }
    }

    private sealed class FakeChatClient : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ChatResponse(new ChatMessage(ChatRole.Assistant, string.Empty)));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.Yield();
            yield return new ChatResponseUpdate(ChatRole.Assistant, string.Empty);
        }

        public object? GetService(Type serviceType, object? serviceKey = null)
        {
            return null;
        }

        public void Dispose()
        {
        }
    }
}
