using BioTwin_AI.AspNetCoreApi.Application.Embeddings;

namespace BioTwin_AI.AspNetCoreApi.Tests.Application;

public sealed class LocalEmbeddingServiceFactoryTests
{
    [Fact]
    public void GetOrCreate_checks_availability_before_creating_model()
    {
        var createCalls = 0;
        using var factory = new LocalEmbeddingServiceFactory(
            canLoad: () => false,
            create: () =>
            {
                createCalls++;
                return new FakeEmbeddingService();
            });

        var exception = Assert.Throws<InvalidOperationException>(() => factory.GetOrCreate());

        Assert.Contains("unavailable", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(0, createCalls);
    }

    [Fact]
    public void GetOrCreate_creates_and_caches_one_local_service()
    {
        var createCalls = 0;
        using var factory = new LocalEmbeddingServiceFactory(
            canLoad: () => true,
            create: () =>
            {
                createCalls++;
                return new FakeEmbeddingService();
            });

        var first = factory.GetOrCreate();
        var second = factory.GetOrCreate();

        Assert.Same(first, second);
        Assert.Equal(1, createCalls);
    }

    private sealed class FakeEmbeddingService : IEmbeddingService, IDisposable
    {
        public Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken) =>
            Task.FromResult(new[] { 1f, 0f });

        public void Dispose()
        {
        }
    }
}
