using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json;
using BioTwin_AI.AspNetCoreApi.Infrastructure.Ai;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using System.ClientModel;

namespace BioTwin_AI.AspNetCoreApi.Application.Llm;

public sealed class LlmChatService : ILlmChatService
{
    private readonly LlmProviderClients _clients;
    private readonly LlmFailoverOptions _options;
    private readonly ILogger<LlmChatService> _logger;
    private readonly ProviderCooldownState _primaryCooldown;

    public LlmChatService(
        LlmProviderClients clients,
        IOptions<LlmFailoverOptions> options,
        TimeProvider timeProvider,
        ILogger<LlmChatService> logger)
    {
        _clients = clients;
        _options = options.Value;
        _logger = logger;
        _primaryCooldown = new ProviderCooldownState(
            timeProvider,
            TimeSpan.FromSeconds(Math.Max(1, _options.FallbackCooldownSeconds)));
    }

    public async Task<string> CompleteAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions options,
        LlmRequestKind requestKind,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var messageList = messages as IReadOnlyList<ChatMessage> ?? messages.ToArray();
        var primary = _clients.Resolve(_options.PrimaryProvider);
        var fallback = _clients.Resolve(_options.FallbackProvider);

        if (primary.IsConfigured && !_primaryCooldown.IsCoolingDown)
        {
            try
            {
                var primaryText = await CompleteProviderAsync(
                    primary,
                    messageList,
                    options,
                    requestKind,
                    cancellationToken);
                if (IsUsable(primaryText, requestKind))
                {
                    _primaryCooldown.MarkHealthy();
                    return primaryText;
                }

                _primaryCooldown.MarkRetryableFailure();
                _logger.LogWarning(
                    "LLM provider returned unusable content. Provider={Provider} Model={Model} RequestKind={RequestKind}",
                    primary.Name,
                    primary.GetModel(requestKind),
                    requestKind);
            }
            catch (Exception ex) when (IsRetryable(ex, cancellationToken))
            {
                _primaryCooldown.MarkRetryableFailure();
                LogProviderFailure(primary, requestKind, ex, fallbackEligible: true);
            }
            catch (Exception ex) when (!IsCallerCancellation(ex, cancellationToken))
            {
                LogProviderFailure(primary, requestKind, ex, fallbackEligible: false);
                throw new LlmResponseException("The primary LLM provider rejected the request.");
            }
        }

        if (!_options.FallbackEnabled)
        {
            throw new InvalidOperationException("The primary LLM provider failed and automatic fallback is disabled.");
        }

        if (!fallback.IsConfigured)
        {
            if (!primary.IsConfigured)
            {
                throw new InvalidOperationException("No LLM provider is configured.");
            }

            throw new InvalidOperationException("The primary LLM provider failed and the fallback provider is not configured.");
        }

        string fallbackText;
        try
        {
            fallbackText = await CompleteProviderAsync(
                fallback,
                messageList,
                options,
                requestKind,
                cancellationToken);
        }
        catch (Exception ex) when (IsRetryable(ex, cancellationToken))
        {
            LogProviderFailure(fallback, requestKind, ex, fallbackEligible: false);
            throw new LlmResponseException("Both LLM providers failed to return a usable response.");
        }
        catch (Exception ex) when (!IsCallerCancellation(ex, cancellationToken))
        {
            LogProviderFailure(fallback, requestKind, ex, fallbackEligible: false);
            throw new LlmResponseException("The fallback LLM provider rejected the request.");
        }

        if (!IsUsable(fallbackText, requestKind))
        {
            throw new LlmResponseException("Both LLM providers returned unusable content.");
        }

        return fallbackText;
    }

    public async IAsyncEnumerable<string> StreamAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions options,
        LlmRequestKind requestKind,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var messageList = messages as IReadOnlyList<ChatMessage> ?? messages.ToArray();
        var primary = _clients.Resolve(_options.PrimaryProvider);
        var fallback = _clients.Resolve(_options.FallbackProvider);

        if (primary.IsConfigured && !_primaryCooldown.IsCoolingDown)
        {
            await using var primaryEnumerator = StreamProviderAsync(
                    primary,
                    messageList,
                    options,
                    requestKind,
                    cancellationToken)
                .GetAsyncEnumerator(cancellationToken);

            string? firstText = null;
            try
            {
                if (await primaryEnumerator.MoveNextAsync())
                {
                    firstText = primaryEnumerator.Current;
                }
                else
                {
                    _primaryCooldown.MarkRetryableFailure();
                }
            }
            catch (Exception ex) when (IsRetryable(ex, cancellationToken))
            {
                _primaryCooldown.MarkRetryableFailure();
                LogProviderFailure(primary, requestKind, ex, fallbackEligible: true);
            }
            catch (Exception ex) when (!IsCallerCancellation(ex, cancellationToken))
            {
                LogProviderFailure(primary, requestKind, ex, fallbackEligible: false);
                throw new LlmResponseException("The primary LLM provider rejected the streaming request.");
            }

            if (firstText is not null)
            {
                _primaryCooldown.MarkHealthy();
                yield return firstText;
                while (true)
                {
                    bool hasNext;
                    try
                    {
                        hasNext = await primaryEnumerator.MoveNextAsync();
                    }
                    catch (Exception ex) when (!IsCallerCancellation(ex, cancellationToken))
                    {
                        if (IsRetryable(ex, cancellationToken))
                        {
                            _primaryCooldown.MarkRetryableFailure();
                        }

                        LogProviderFailure(primary, requestKind, ex, fallbackEligible: false);
                        throw new LlmResponseException("The LLM stream failed after output began.");
                    }

                    if (!hasNext)
                    {
                        break;
                    }

                    yield return primaryEnumerator.Current;
                }

                yield break;
            }
        }

        if (!_options.FallbackEnabled)
        {
            throw new InvalidOperationException("The primary LLM provider failed and automatic fallback is disabled.");
        }

        if (!fallback.IsConfigured)
        {
            if (!primary.IsConfigured)
            {
                throw new InvalidOperationException("No LLM provider is configured.");
            }

            throw new InvalidOperationException("The primary LLM provider failed and the fallback provider is not configured.");
        }

        var emittedFallbackText = false;
        await using var fallbackEnumerator = StreamProviderAsync(
                fallback,
                messageList,
                options,
                requestKind,
                cancellationToken)
            .GetAsyncEnumerator(cancellationToken);
        while (true)
        {
            bool hasNext;
            try
            {
                hasNext = await fallbackEnumerator.MoveNextAsync();
            }
            catch (Exception ex) when (!IsCallerCancellation(ex, cancellationToken))
            {
                LogProviderFailure(fallback, requestKind, ex, fallbackEligible: false);
                throw new LlmResponseException("The fallback LLM stream failed.");
            }

            if (!hasNext)
            {
                break;
            }

            emittedFallbackText = true;
            yield return fallbackEnumerator.Current;
        }

        if (!emittedFallbackText)
        {
            throw new InvalidOperationException("Both LLM providers returned unusable content.");
        }
    }

    private async Task<string> CompleteProviderAsync(
        LlmProviderClient provider,
        IReadOnlyList<ChatMessage> messages,
        ChatOptions options,
        LlmRequestKind requestKind,
        CancellationToken cancellationToken)
    {
        var providerOptions = options.Clone();
        providerOptions.ModelId = provider.GetModel(requestKind);
        using var timeout = CreateTimeout(requestKind, cancellationToken);
        var response = await provider.Client!.GetResponseAsync(messages, providerOptions, timeout.Token);
        var text = response.Text.Trim();
        if (text.Length == 0)
        {
            _logger.LogWarning(
                "LLM returned an empty completion. Provider={Provider} Model={ModelId} FinishReason={FinishReason} ResponseId={ResponseId} InputTokens={InputTokens} OutputTokens={OutputTokens} ReasoningTokens={ReasoningTokens}",
                provider.Name,
                response.ModelId,
                response.FinishReason?.Value,
                response.ResponseId,
                response.Usage?.InputTokenCount,
                response.Usage?.OutputTokenCount,
                response.Usage?.ReasoningTokenCount);
        }

        return text;
    }

    private async IAsyncEnumerable<string> StreamProviderAsync(
        LlmProviderClient provider,
        IReadOnlyList<ChatMessage> messages,
        ChatOptions options,
        LlmRequestKind requestKind,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var providerOptions = options.Clone();
        providerOptions.ModelId = provider.GetModel(requestKind);
        using var timeout = CreateTimeout(requestKind, cancellationToken);

        await foreach (var update in provider.Client!.GetStreamingResponseAsync(
            messages,
            providerOptions,
            timeout.Token))
        {
            var emittedContent = false;
            foreach (var content in update.Contents)
            {
                if (content is TextContent textContent && !string.IsNullOrEmpty(textContent.Text))
                {
                    emittedContent = true;
                    yield return textContent.Text;
                }
            }

            if (!emittedContent && !string.IsNullOrEmpty(update.Text))
            {
                yield return update.Text;
            }
        }
    }

    private CancellationTokenSource CreateTimeout(
        LlmRequestKind requestKind,
        CancellationToken cancellationToken)
    {
        var seconds = requestKind == LlmRequestKind.StructuredExtraction
            ? _options.ExtractionTimeoutSeconds
            : _options.RequestTimeoutSeconds;
        var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, seconds)));
        return timeout;
    }

    private static bool IsUsable(string text, LlmRequestKind requestKind)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        if (requestKind != LlmRequestKind.StructuredExtraction)
        {
            return true;
        }

        try
        {
            using var document = JsonDocument.Parse(text);
            return document.RootElement.ValueKind == JsonValueKind.Object;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static bool IsRetryable(Exception exception, CancellationToken callerToken)
    {
        if (exception is OperationCanceledException)
        {
            return !callerToken.IsCancellationRequested;
        }

        if (exception is TimeoutException)
        {
            return true;
        }

        if (exception is LlmProviderResponseException)
        {
            return true;
        }

        if (exception is HttpRequestException httpException)
        {
            return httpException.StatusCode is null || IsRetryableStatus((int)httpException.StatusCode.Value);
        }

        return exception is ClientResultException clientException &&
            IsRetryableStatus(clientException.Status);
    }

    private static bool IsCallerCancellation(Exception exception, CancellationToken callerToken)
    {
        return exception is OperationCanceledException && callerToken.IsCancellationRequested;
    }

    private static bool IsRetryableStatus(int status)
    {
        return status is (int)HttpStatusCode.RequestTimeout or (int)HttpStatusCode.TooManyRequests ||
            status is >= (int)HttpStatusCode.InternalServerError and <= 599;
    }

    private void LogProviderFailure(
        LlmProviderClient provider,
        LlmRequestKind requestKind,
        Exception exception,
        bool fallbackEligible)
    {
        var status = exception switch
        {
            HttpRequestException httpException => (int?)httpException.StatusCode,
            ClientResultException clientException => clientException.Status,
            _ => null
        };
        _logger.LogWarning(
            "LLM provider failed. Provider={Provider} Model={Model} RequestKind={RequestKind} StatusCode={StatusCode} ExceptionType={ExceptionType} FallbackEligible={FallbackEligible}",
            provider.Name,
            provider.GetModel(requestKind),
            requestKind,
            status,
            exception.GetType().Name,
            fallbackEligible);
    }
}
