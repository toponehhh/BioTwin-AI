namespace BioTwin_AI.AspNetCoreApi.Application.Embeddings;

public sealed class LocalEmbeddingServiceFactory : ILocalEmbeddingServiceFactory, IDisposable
{
    private readonly object _gate = new();
    private readonly Func<bool> _canLoad;
    private readonly Func<IEmbeddingService> _create;
    private IEmbeddingService? _service;

    public LocalEmbeddingServiceFactory(
        IHostEnvironment environment,
        IConfiguration configuration,
        ILogger<BgeM3OnnxEmbeddingService> logger)
        : this(
            () => BgeM3OnnxEmbeddingService.CanLoad(environment, configuration),
            () => new BgeM3OnnxEmbeddingService(environment, configuration, logger))
    {
    }

    public LocalEmbeddingServiceFactory(
        Func<bool> canLoad,
        Func<IEmbeddingService> create)
    {
        _canLoad = canLoad;
        _create = create;
    }

    public IEmbeddingService GetOrCreate()
    {
        if (_service is not null)
        {
            return _service;
        }

        lock (_gate)
        {
            if (_service is not null)
            {
                return _service;
            }

            if (!_canLoad())
            {
                throw new InvalidOperationException("Local BGE-M3 embedding model files are unavailable.");
            }

            _service = _create();
            return _service;
        }
    }

    public void Dispose()
    {
        if (_service is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }
}
