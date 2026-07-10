using BioTwin_AI.AspNetCoreApi.Application.Reranking;

namespace BioTwin_AI.AspNetCoreApi.Tests.Application;

public sealed class LocalRerankServiceFactoryTests
{
    [Fact]
    public void GetOrCreate_checks_model_files_before_construction()
    {
        var creates = 0;
        using var factory = new LocalRerankServiceFactory(
            canLoad: () => false,
            create: () =>
            {
                creates++;
                return new FakeRerankService();
            });

        Assert.Throws<InvalidOperationException>(() => factory.GetOrCreate());
        Assert.Equal(0, creates);
    }

    [Fact]
    public void GetOrCreate_caches_one_local_service()
    {
        var creates = 0;
        using var factory = new LocalRerankServiceFactory(
            canLoad: () => true,
            create: () =>
            {
                creates++;
                return new FakeRerankService();
            });

        Assert.Same(factory.GetOrCreate(), factory.GetOrCreate());
        Assert.Equal(1, creates);
    }

    private sealed class FakeRerankService : IRerankService, IDisposable
    {
        public Task<IReadOnlyList<RerankResult>> RerankAsync(string query, IReadOnlyList<string> documents, int limit, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<RerankResult>>([]);

        public void Dispose()
        {
        }
    }
}
