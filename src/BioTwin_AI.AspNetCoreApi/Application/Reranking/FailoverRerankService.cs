using System.Net;
using BioTwin_AI.AspNetCoreApi.Infrastructure.Ai;
using Microsoft.Extensions.Options;

namespace BioTwin_AI.AspNetCoreApi.Application.Reranking;

public sealed class FailoverRerankService : IRerankService
{
    private readonly IRerankService _primary;
    private readonly ILocalRerankServiceFactory _localFactory;
    private readonly RerankFailoverOptions _options;
    private readonly ILogger<FailoverRerankService> _logger;
    private readonly ProviderCooldownState _cooldown;
    private readonly string _cloudflareModel;

    public FailoverRerankService(
        IRerankService primary,
        ILocalRerankServiceFactory localFactory,
        IOptions<RerankFailoverOptions> options,
        IOptions<CloudflareAiOptions> cloudflareOptions,
        TimeProvider timeProvider,
        ILogger<FailoverRerankService> logger)
    {
        _primary = primary;
        _localFactory = localFactory;
        _options = options.Value;
        _cloudflareModel = cloudflareOptions.Value.RerankModel;
        _logger = logger;
        _cooldown = new ProviderCooldownState(
            timeProvider,
            TimeSpan.FromSeconds(Math.Max(1, _options.FallbackCooldownSeconds)));
    }

    public async Task<IReadOnlyList<RerankResult>> RerankAsync(
        string query,
        IReadOnlyList<string> documents,
        int limit,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        if (documents.Count == 0 || limit <= 0)
        {
            return [];
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (!_options.Enabled)
        {
            return [];
        }

        if (!_cooldown.IsCoolingDown)
        {
            try
            {
                var results = await _primary.RerankAsync(query, documents, limit, cancellationToken);
                if (IsValid(results, documents.Count, limit))
                {
                    _cooldown.MarkHealthy();
                    return results;
                }

                throw new RerankResponseException("Cloudflare reranking returned invalid results.");
            }
            catch (Exception ex) when (IsRetryable(ex, cancellationToken))
            {
                _cooldown.MarkRetryableFailure();
                LogFailure("CloudflareWorkersAI", _cloudflareModel, ex, fallbackEligible: true);
            }
            catch (Exception ex) when (!IsCallerCancellation(ex, cancellationToken))
            {
                LogFailure("CloudflareWorkersAI", _cloudflareModel, ex, fallbackEligible: false);
                return [];
            }
        }

        if (!_options.FallbackEnabled)
        {
            return [];
        }

        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var local = _localFactory.GetOrCreate();
            var results = await local.RerankAsync(query, documents, limit, cancellationToken);
            return IsValid(results, documents.Count, limit) ? results : [];
        }
        catch (Exception ex) when (!IsCallerCancellation(ex, cancellationToken))
        {
            LogFailure("BgeRerankerOnnx", "bge-reranker-v2-m3-onnx", ex, fallbackEligible: false);
            return [];
        }
    }

    private static bool IsValid(IReadOnlyList<RerankResult> results, int documentCount, int limit)
    {
        if (results.Count == 0 || results.Count > Math.Min(documentCount, limit))
        {
            return false;
        }

        var seen = new HashSet<int>();
        return results.All(result =>
            result.Index >= 0 &&
            result.Index < documentCount &&
            double.IsFinite(result.Score) &&
            seen.Add(result.Index));
    }

    private static bool IsRetryable(Exception exception, CancellationToken callerToken)
    {
        if (exception is OperationCanceledException)
        {
            return !callerToken.IsCancellationRequested;
        }

        if (exception is RerankResponseException or TimeoutException)
        {
            return true;
        }

        if (exception is not HttpRequestException httpException)
        {
            return false;
        }

        return true;
    }

    private static bool IsCallerCancellation(Exception exception, CancellationToken callerToken)
    {
        return exception is OperationCanceledException && callerToken.IsCancellationRequested;
    }

    private void LogFailure(string provider, string model, Exception exception, bool fallbackEligible)
    {
        var status = exception is HttpRequestException httpException
            ? (int?)httpException.StatusCode
            : null;
        _logger.LogWarning(
            "Rerank provider failed. Provider={Provider} Model={Model} StatusCode={StatusCode} ExceptionType={ExceptionType} FallbackEligible={FallbackEligible}",
            provider,
            model,
            status,
            exception.GetType().Name,
            fallbackEligible);
    }
}
