using System.Net;
using BioTwin_AI.AspNetCoreApi.Infrastructure.Ai;
using Microsoft.Extensions.Options;

namespace BioTwin_AI.AspNetCoreApi.Application.Embeddings;

public sealed class FailoverEmbeddingService : IEmbeddingService
{
    private readonly IEmbeddingService _primary;
    private readonly ILocalEmbeddingServiceFactory _localFactory;
    private readonly EmbeddingFailoverOptions _options;
    private readonly ILogger<FailoverEmbeddingService> _logger;
    private readonly ProviderCooldownState _cooldown;

    public FailoverEmbeddingService(
        IEmbeddingService primary,
        ILocalEmbeddingServiceFactory localFactory,
        IOptions<EmbeddingFailoverOptions> options,
        TimeProvider timeProvider,
        ILogger<FailoverEmbeddingService> logger)
    {
        _primary = primary;
        _localFactory = localFactory;
        _options = options.Value;
        _logger = logger;
        _cooldown = new ProviderCooldownState(
            timeProvider,
            TimeSpan.FromSeconds(Math.Max(1, _options.FallbackCooldownSeconds)));
    }

    public async Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        cancellationToken.ThrowIfCancellationRequested();

        if (!_cooldown.IsCoolingDown)
        {
            try
            {
                var primaryVector = await _primary.EmbedAsync(text, cancellationToken);
                var validated = EmbeddingVectorValidator.ValidateAndNormalize(
                    primaryVector,
                    _options.ExpectedDimensions);
                _cooldown.MarkHealthy();
                return validated;
            }
            catch (Exception ex) when (IsRetryable(ex, cancellationToken))
            {
                _cooldown.MarkRetryableFailure();
                LogFailure("CloudflareWorkersAI", ex, fallbackEligible: true);
            }
            catch (Exception ex) when (!IsCallerCancellation(ex, cancellationToken))
            {
                LogFailure("CloudflareWorkersAI", ex, fallbackEligible: false);
                throw new EmbeddingProviderException("The primary embedding provider rejected the request.");
            }
        }

        if (!_options.FallbackEnabled)
        {
            throw new EmbeddingProviderException(
                "The primary embedding provider failed and local fallback is disabled.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            var local = _localFactory.GetOrCreate();
            var localVector = await local.EmbedAsync(text, cancellationToken);
            return EmbeddingVectorValidator.ValidateAndNormalize(
                localVector,
                _options.ExpectedDimensions);
        }
        catch (Exception ex) when (!IsCallerCancellation(ex, cancellationToken))
        {
            LogFailure("BgeM3Onnx", ex, fallbackEligible: false);
            throw new EmbeddingProviderException("Both embedding providers failed.");
        }
    }

    private static bool IsRetryable(Exception exception, CancellationToken callerToken)
    {
        if (exception is OperationCanceledException)
        {
            return !callerToken.IsCancellationRequested;
        }

        if (exception is EmbeddingResponseException or TimeoutException)
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

    private void LogFailure(string provider, Exception exception, bool fallbackEligible)
    {
        var status = exception is HttpRequestException httpException
            ? (int?)httpException.StatusCode
            : null;
        _logger.LogWarning(
            "Embedding provider failed. Provider={Provider} StatusCode={StatusCode} ExceptionType={ExceptionType} FallbackEligible={FallbackEligible}",
            provider,
            status,
            exception.GetType().Name,
            fallbackEligible);
    }
}
