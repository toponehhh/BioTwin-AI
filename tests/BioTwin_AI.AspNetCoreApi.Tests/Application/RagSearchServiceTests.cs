using BioTwin_AI.AspNetCoreApi.Application.Embeddings;
using BioTwin_AI.AspNetCoreApi.Application.Rag;
using BioTwin_AI.AspNetCoreApi.Application.Reranking;
using BioTwin_AI.AspNetCoreApi.Infrastructure.Ai;
using BioTwin_AI.AspNetCoreApi.Infrastructure.Data;
using BioTwin_AI.AspNetCoreApi.Infrastructure.Data.Entities;
using BioTwin_AI.DotNetShared.Rag;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace BioTwin_AI.AspNetCoreApi.Tests.Application;

public sealed class RagSearchServiceTests
{
    [Fact]
    public async Task SearchAsync_reranks_the_coarse_candidate_set_before_applying_limit()
    {
        await using var fixture = await RagFixture.CreateAsync();
        var reranker = new FakeRerankService(
            [new RerankResult(1, 0.95), new RerankResult(0, 0.75)]);
        var service = fixture.CreateService(reranker, candidateCount: 3);

        var response = await service.SearchAsync(
            "tenant-1",
            false,
            new RagSearchRequest("backend", 2),
            CancellationToken.None);

        Assert.Equal(
            [fixture.Sections[1].Id, fixture.Sections[0].Id],
            response.Results.Select(result => result.ResumeSectionId));
        Assert.Equal([0.95f, 0.75f], response.Results.Select(result => result.Score));
        Assert.Equal(3, reranker.Documents.Count);
        Assert.Contains("Second complete section content", reranker.Documents[1], StringComparison.Ordinal);
    }

    [Fact]
    public async Task SearchAsync_preserves_coarse_order_when_reranker_returns_empty()
    {
        await using var fixture = await RagFixture.CreateAsync();
        var reranker = new FakeRerankService([]);
        var service = fixture.CreateService(reranker, candidateCount: 3);

        var response = await service.SearchAsync(
            "tenant-1",
            false,
            new RagSearchRequest("backend", 2),
            CancellationToken.None);

        Assert.Equal(
            [fixture.Sections[0].Id, fixture.Sections[1].Id],
            response.Results.Select(result => result.ResumeSectionId));
        Assert.Equal(3, reranker.Documents.Count);
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("non-finite")]
    [InlineData("out-of-range")]
    public async Task SearchAsync_discards_the_entire_rerank_batch_when_results_are_invalid(string invalidKind)
    {
        await using var fixture = await RagFixture.CreateAsync();
        IReadOnlyList<RerankResult> invalidResults = invalidKind switch
        {
            "duplicate" => [new RerankResult(1, 0.9), new RerankResult(1, 0.8)],
            "non-finite" => [new RerankResult(1, double.NaN)],
            "out-of-range" => [new RerankResult(99, 0.8)],
            _ => throw new ArgumentOutOfRangeException(nameof(invalidKind))
        };
        var reranker = new FakeRerankService(invalidResults);
        var service = fixture.CreateService(reranker, candidateCount: 3);

        var response = await service.SearchAsync(
            "tenant-1",
            false,
            new RagSearchRequest("backend", 2),
            CancellationToken.None);

        Assert.Equal(
            [fixture.Sections[0].Id, fixture.Sections[1].Id],
            response.Results.Select(result => result.ResumeSectionId));
    }

    [Fact]
    public async Task SearchAsync_clamps_requested_limit_and_candidate_count()
    {
        await using var fixture = await RagFixture.CreateAsync(extraSectionCount: 102);
        var reranker = new FakeRerankService([]);
        var service = fixture.CreateService(reranker, candidateCount: 200);

        var response = await service.SearchAsync(
            "tenant-1",
            false,
            new RagSearchRequest("backend", 50),
            CancellationToken.None);

        Assert.Equal(100, reranker.Documents.Count);
        Assert.Equal(20, reranker.RequestedLimit);
        Assert.Equal(20, response.Results.Count);

        reranker = new FakeRerankService([]);
        service = fixture.CreateService(reranker, candidateCount: 1);
        await service.SearchAsync(
            "tenant-1",
            false,
            new RagSearchRequest("backend", 2),
            CancellationToken.None);
        Assert.Equal(2, reranker.Documents.Count);
        Assert.Equal(2, reranker.RequestedLimit);
    }

    [Fact]
    public async Task SearchAsync_respects_tenant_scope_and_include_all_tenants()
    {
        await using var fixture = await RagFixture.CreateAsync();
        var reranker = new FakeRerankService([]);
        var service = fixture.CreateService(reranker, candidateCount: 10);

        await service.SearchAsync(
            "tenant-1",
            false,
            new RagSearchRequest("backend", 5),
            CancellationToken.None);
        Assert.DoesNotContain(reranker.Documents, text =>
            text.Contains("Foreign tenant content", StringComparison.Ordinal));

        await service.SearchAsync(
            "tenant-1",
            true,
            new RagSearchRequest("backend", 5),
            CancellationToken.None);
        Assert.Contains(reranker.Documents, text =>
            text.Contains("Foreign tenant content", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SearchAsync_propagates_cancellation_and_keeps_full_text_out_of_preview()
    {
        await using var fixture = await RagFixture.CreateAsync();
        var reranker = new FakeRerankService([]);
        var service = fixture.CreateService(reranker, candidateCount: 3);

        var response = await service.SearchAsync(
            "tenant-1",
            false,
            new RagSearchRequest("backend", 1),
            CancellationToken.None);
        Assert.Contains("FULL_TEXT_TAIL", reranker.Documents[0], StringComparison.Ordinal);
        Assert.DoesNotContain("FULL_TEXT_TAIL", response.Results[0].ContentPreview, StringComparison.Ordinal);
        Assert.EndsWith("...", response.Results[0].ContentPreview, StringComparison.Ordinal);

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.SearchAsync(
            "tenant-1",
            false,
            new RagSearchRequest("backend", 1),
            cancellation.Token));
    }

    private sealed class FakeRerankService(IReadOnlyList<RerankResult> results) : IRerankService
    {
        public IReadOnlyList<string> Documents { get; private set; } = [];
        public int RequestedLimit { get; private set; }

        public Task<IReadOnlyList<RerankResult>> RerankAsync(
            string query,
            IReadOnlyList<string> documents,
            int limit,
            CancellationToken cancellationToken = default)
        {
            Documents = documents;
            RequestedLimit = limit;
            return Task.FromResult(results);
        }
    }

    private sealed class FixedEmbeddingService : IEmbeddingService
    {
        public Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken) =>
            Task.FromResult(new[] { 1f, 0f });
    }

    private sealed class RagFixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        private readonly BioTwinApiDbContext context;

        private RagFixture(
            SqliteConnection connection,
            BioTwinApiDbContext context,
            IReadOnlyList<ResumeSection> sections)
        {
            this.connection = connection;
            this.context = context;
            Sections = sections;
        }

        public IReadOnlyList<ResumeSection> Sections { get; }

        public static async Task<RagFixture> CreateAsync(int extraSectionCount = 0)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var options = new DbContextOptionsBuilder<BioTwinApiDbContext>()
                .UseSqlite(connection)
                .Options;
            var context = new BioTwinApiDbContext(options);
            await context.Database.EnsureCreatedAsync();

            var entry = new ResumeEntry
            {
                TenantId = "tenant-1",
                Language = "en",
                Title = "Synthetic Resume"
            };
            var sections = new List<ResumeSection>
            {
                CreateSection(entry, "First", new string('A', 400) + " FULL_TEXT_TAIL backend", 0, [1f, 0f]),
                CreateSection(entry, "Second", "Second complete section content", 1, [0.8f, 0.2f]),
                CreateSection(entry, "Third", "Third unrelated section content", 2, [0f, 1f])
            };
            for (var index = 0; index < extraSectionCount; index++)
            {
                sections.Add(CreateSection(
                    entry,
                    $"Extra {index}",
                    $"Extra backend candidate {index}",
                    index + 3,
                    [0f, 1f]));
            }

            entry.Sections.AddRange(sections);
            var foreignEntry = new ResumeEntry
            {
                TenantId = "tenant-2",
                Language = "en",
                Title = "Foreign Resume"
            };
            foreignEntry.Sections.Add(CreateSection(
                foreignEntry,
                "Foreign",
                "Foreign tenant content backend",
                0,
                [1f, 0f]));
            context.ResumeEntries.AddRange(entry, foreignEntry);
            await context.SaveChangesAsync();
            return new RagFixture(connection, context, sections);
        }

        public RagSearchService CreateService(IRerankService reranker, int candidateCount)
        {
            return new RagSearchService(
                context,
                new FixedEmbeddingService(),
                reranker,
                Options.Create(new RerankFailoverOptions { CandidateCount = candidateCount }));
        }

        public async ValueTask DisposeAsync()
        {
            await context.DisposeAsync();
            await connection.DisposeAsync();
        }

        private static ResumeSection CreateSection(
            ResumeEntry entry,
            string title,
            string content,
            int sortOrder,
            float[] vector)
        {
            var section = new ResumeSection
            {
                ResumeEntry = entry,
                TenantId = entry.TenantId,
                Title = title,
                Content = content,
                SortOrder = sortOrder
            };
            section.Vector = new ResumeSectionVector
            {
                ResumeSection = section,
                TenantId = entry.TenantId,
                ResumeTitle = entry.Title,
                SectionTitle = title,
                Content = content,
                EmbeddingPayload = EmbeddingPayloadSerializer.Serialize(vector)
            };
            return section;
        }
    }
}
