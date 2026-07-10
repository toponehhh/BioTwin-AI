namespace BioTwin_AI.AspNetCoreApi.Application.Reranking;

public sealed class LocalRerankServiceFactory : ILocalRerankServiceFactory, IDisposable
{
    private readonly object _gate = new();
    private readonly Func<bool> _canLoad;
    private readonly Func<IRerankService> _create;
    private IRerankService? _service;

    public LocalRerankServiceFactory(
        IHostEnvironment environment,
        IConfiguration configuration,
        ILogger<LocalBgeRerankService> logger)
        : this(
            () => LocalBgeRerankService.CanLoad(environment, configuration),
            () => new LocalBgeRerankService(environment, configuration, logger))
    {
    }

    public LocalRerankServiceFactory(Func<bool> canLoad, Func<IRerankService> create)
    {
        _canLoad = canLoad;
        _create = create;
    }

    public IRerankService GetOrCreate()
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
                throw new InvalidOperationException("Local BGE rerank model files are unavailable.");
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
