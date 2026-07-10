using System.Net;
using System.Runtime.CompilerServices;
using BioTwin_AI.AspNetCoreApi.Application.Llm;
using BioTwin_AI.AspNetCoreApi.Infrastructure.Ai;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace BioTwin_AI.AspNetCoreApi.Tests.Application;

public sealed class LlmChatServiceTests
{
    [Fact]
    public async Task CompleteAsync_uses_Cloudflare_without_calling_OpenRouter()
    {
        var cloudflare = new FakeChatClient { ResponseText = "primary response" };
        var openRouter = new FakeChatClient { ResponseText = "fallback response" };
        var service = CreateService(cloudflare, openRouter);

        var result = await service.CompleteAsync(
            [new ChatMessage(ChatRole.User, "hello")],
            new ChatOptions(),
            LlmRequestKind.General,
            CancellationToken.None);

        Assert.Equal("primary response", result);
        Assert.Equal("@cf/chat", Assert.Single(cloudflare.Options).ModelId);
        Assert.Empty(openRouter.Options);
    }

    [Fact]
    public async Task CompleteAsync_falls_back_when_primary_JSON_is_invalid()
    {
        var cloudflare = new FakeChatClient { ResponseText = "not-json" };
        var openRouter = new FakeChatClient { ResponseText = "{\"title\":\"Resume\"}" };
        var service = CreateService(cloudflare, openRouter);

        var result = await service.CompleteAsync(
            [new ChatMessage(ChatRole.User, "private resume")],
            new ChatOptions { ResponseFormat = ChatResponseFormat.Json },
            LlmRequestKind.StructuredExtraction,
            CancellationToken.None);

        Assert.Equal("{\"title\":\"Resume\"}", result);
        Assert.Equal("@cf/extraction", Assert.Single(cloudflare.Options).ModelId);
        Assert.Equal("openrouter/extraction", Assert.Single(openRouter.Options).ModelId);
    }

    [Theory]
    [InlineData(408)]
    [InlineData(429)]
    [InlineData(500)]
    [InlineData(503)]
    [InlineData(599)]
    public async Task CompleteAsync_falls_back_for_retryable_HTTP_status(int statusCode)
    {
        var cloudflare = new FakeChatClient
        {
            CompleteException = new HttpRequestException("provider failed", null, (HttpStatusCode)statusCode)
        };
        var openRouter = new FakeChatClient { ResponseText = "fallback response" };
        var service = CreateService(cloudflare, openRouter);

        var result = await service.CompleteAsync(
            [new ChatMessage(ChatRole.User, "hello")],
            new ChatOptions(),
            LlmRequestKind.General,
            CancellationToken.None);

        Assert.Equal("fallback response", result);
        Assert.Single(cloudflare.Options);
        Assert.Single(openRouter.Options);
    }

    [Fact]
    public async Task CompleteAsync_does_not_fall_back_for_status_outside_HTTP_5xx_range()
    {
        var cloudflare = new FakeChatClient
        {
            CompleteException = new HttpRequestException("nonstandard", null, (HttpStatusCode)600)
        };
        var openRouter = new FakeChatClient { ResponseText = "fallback response" };
        var service = CreateService(cloudflare, openRouter);

        await Assert.ThrowsAsync<LlmResponseException>(() => service.CompleteAsync(
            [new ChatMessage(ChatRole.User, "hello")],
            new ChatOptions(),
            LlmRequestKind.General,
            CancellationToken.None));

        Assert.Empty(openRouter.Options);
    }

    [Fact]
    public async Task CompleteAsync_does_not_attach_provider_exception_content_to_logs()
    {
        var logger = new RecordingLogger<LlmChatService>();
        var cloudflare = new FakeChatClient
        {
            CompleteException = new HttpRequestException(
                "private provider response body",
                null,
                HttpStatusCode.ServiceUnavailable)
        };
        var service = CreateService(
            cloudflare,
            new FakeChatClient { ResponseText = "fallback" },
            logger);

        await service.CompleteAsync(
            [new ChatMessage(ChatRole.User, "private prompt")],
            new ChatOptions(),
            LlmRequestKind.General,
            CancellationToken.None);

        Assert.All(logger.Entries, entry => Assert.Null(entry.Exception));
        Assert.DoesNotContain(logger.Entries, entry =>
            entry.Message.Contains("private provider response body", StringComparison.Ordinal) ||
            entry.Message.Contains("private prompt", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CompleteAsync_does_not_fall_back_after_caller_cancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var cloudflare = new FakeChatClient { ResponseText = "unused" };
        var openRouter = new FakeChatClient { ResponseText = "fallback response" };
        var service = CreateService(cloudflare, openRouter);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.CompleteAsync(
            [new ChatMessage(ChatRole.User, "hello")],
            new ChatOptions(),
            LlmRequestKind.General,
            cancellation.Token));

        Assert.Empty(openRouter.Options);
    }

    [Fact]
    public async Task CompleteAsync_skips_primary_during_cooldown()
    {
        var cloudflare = new FakeChatClient
        {
            CompleteException = new HttpRequestException("unavailable", null, HttpStatusCode.ServiceUnavailable)
        };
        var openRouter = new FakeChatClient { ResponseText = "fallback response" };
        var service = CreateService(cloudflare, openRouter);

        await service.CompleteAsync(
            [new ChatMessage(ChatRole.User, "first")],
            new ChatOptions(),
            LlmRequestKind.General,
            CancellationToken.None);
        cloudflare.CompleteException = null;
        cloudflare.ResponseText = "primary recovered";
        await service.CompleteAsync(
            [new ChatMessage(ChatRole.User, "second")],
            new ChatOptions(),
            LlmRequestKind.General,
            CancellationToken.None);

        Assert.Single(cloudflare.Options);
        Assert.Equal(2, openRouter.Options.Count);
    }

    [Fact]
    public async Task CompleteAsync_reports_when_no_provider_is_configured()
    {
        var service = CreateService(null, null);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => service.CompleteAsync(
            [new ChatMessage(ChatRole.User, "hello")],
            new ChatOptions(),
            LlmRequestKind.General,
            CancellationToken.None));

        Assert.Contains("No LLM provider is configured", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CompleteAsync_does_not_use_fallback_when_it_is_disabled_and_primary_is_missing()
    {
        var openRouter = new FakeChatClient { ResponseText = "fallback response" };
        var service = CreateService(null, openRouter, fallbackEnabled: false);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.CompleteAsync(
            [new ChatMessage(ChatRole.User, "hello")],
            new ChatOptions(),
            LlmRequestKind.General,
            CancellationToken.None));

        Assert.Empty(openRouter.Options);
    }

    [Fact]
    public async Task CompleteAsync_reports_a_response_error_when_both_JSON_results_are_invalid()
    {
        var service = CreateService(
            new FakeChatClient { ResponseText = "not-json" },
            new FakeChatClient { ResponseText = "still-not-json" });

        await Assert.ThrowsAsync<LlmResponseException>(() => service.CompleteAsync(
            [new ChatMessage(ChatRole.User, "resume")],
            new ChatOptions { ResponseFormat = ChatResponseFormat.Json },
            LlmRequestKind.StructuredExtraction,
            CancellationToken.None));
    }

    [Fact]
    public async Task CompleteAsync_does_not_expose_fallback_exception_as_an_inner_exception()
    {
        var service = CreateService(
            new FakeChatClient
            {
                CompleteException = new HttpRequestException(
                    "private primary response",
                    null,
                    HttpStatusCode.ServiceUnavailable)
            },
            new FakeChatClient
            {
                CompleteException = new HttpRequestException(
                    "private fallback response",
                    null,
                    HttpStatusCode.ServiceUnavailable)
            });

        var exception = await Assert.ThrowsAsync<LlmResponseException>(() => service.CompleteAsync(
            [new ChatMessage(ChatRole.User, "resume")],
            new ChatOptions(),
            LlmRequestKind.General,
            CancellationToken.None));

        Assert.Null(exception.InnerException);
        Assert.DoesNotContain("private", exception.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StreamAsync_falls_back_before_the_first_text_chunk()
    {
        var cloudflare = new FakeChatClient
        {
            StreamExceptionBeforeFirst = new HttpRequestException("unavailable", null, HttpStatusCode.ServiceUnavailable)
        };
        var openRouter = new FakeChatClient { StreamTexts = ["fallback", " stream"] };
        var service = CreateService(cloudflare, openRouter);

        var result = await CollectAsync(service.StreamAsync(
            [new ChatMessage(ChatRole.User, "hello")],
            new ChatOptions(),
            LlmRequestKind.General,
            CancellationToken.None));

        Assert.Equal(["fallback", " stream"], result);
        Assert.Equal(1, cloudflare.StreamingCalls);
        Assert.Equal(1, openRouter.StreamingCalls);
    }

    [Fact]
    public async Task StreamAsync_does_not_fall_back_after_text_has_started()
    {
        var cloudflare = new FakeChatClient
        {
            StreamTexts = ["primary"],
            StreamExceptionAfterText = new HttpRequestException("stream failed", null, HttpStatusCode.ServiceUnavailable)
        };
        var openRouter = new FakeChatClient { StreamTexts = ["fallback"] };
        var service = CreateService(cloudflare, openRouter);

        var exception = await Assert.ThrowsAsync<LlmResponseException>(() => CollectAsync(service.StreamAsync(
            [new ChatMessage(ChatRole.User, "hello")],
            new ChatOptions(),
            LlmRequestKind.General,
            CancellationToken.None)));

        Assert.Null(exception.InnerException);
        Assert.Equal(0, openRouter.StreamingCalls);
    }

    [Fact]
    public async Task StreamAsync_retryable_failure_after_text_starts_puts_primary_in_cooldown()
    {
        var cloudflare = new FakeChatClient
        {
            StreamTexts = ["partial"],
            StreamExceptionAfterText = new HttpRequestException(
                "stream failed",
                null,
                HttpStatusCode.ServiceUnavailable)
        };
        var openRouter = new FakeChatClient { ResponseText = "fallback response" };
        var service = CreateService(cloudflare, openRouter);

        await Assert.ThrowsAsync<LlmResponseException>(() => CollectAsync(service.StreamAsync(
            [new ChatMessage(ChatRole.User, "first")],
            new ChatOptions(),
            LlmRequestKind.General,
            CancellationToken.None)));
        cloudflare.StreamExceptionAfterText = null;
        cloudflare.ResponseText = "primary response";

        var result = await service.CompleteAsync(
            [new ChatMessage(ChatRole.User, "second")],
            new ChatOptions(),
            LlmRequestKind.General,
            CancellationToken.None);

        Assert.Equal("fallback response", result);
        Assert.Single(cloudflare.Options);
        Assert.Single(openRouter.Options);
    }

    [Fact]
    public async Task CompleteAsync_logs_safe_response_metadata_when_content_is_empty()
    {
        var response = new ChatResponse(new ChatMessage(ChatRole.Assistant, string.Empty))
        {
            ModelId = "reasoning-model",
            ResponseId = "gen-test",
            FinishReason = ChatFinishReason.Length,
            Usage = new UsageDetails
            {
                InputTokenCount = 6000,
                OutputTokenCount = 5000,
                ReasoningTokenCount = 5000
            }
        };
        var logger = new RecordingLogger<LlmChatService>();
        var service = CreateService(
            new FakeChatClient { Response = response },
            new FakeChatClient { ResponseText = "fallback" },
            logger);

        await service.CompleteAsync(
            [new ChatMessage(ChatRole.User, "private prompt")],
            new ChatOptions(),
            LlmRequestKind.General,
            CancellationToken.None);

        Assert.Contains(logger.Entries, entry =>
            entry.Level == LogLevel.Warning &&
            Equals(entry.Values["Provider"], "CloudflareWorkersAI") &&
            !entry.Message.Contains("private prompt", StringComparison.Ordinal));
    }

    private static LlmChatService CreateService(
        FakeChatClient? cloudflare,
        FakeChatClient? openRouter,
        ILogger<LlmChatService>? logger = null,
        bool fallbackEnabled = true)
    {
        return new LlmChatService(
            new LlmProviderClients(
                new LlmProviderClient("CloudflareWorkersAI", cloudflare, "@cf/chat", "@cf/extraction"),
                new LlmProviderClient("OpenRouter", openRouter, "openrouter/chat", "openrouter/extraction")),
            Options.Create(new LlmFailoverOptions
            {
                PrimaryProvider = "CloudflareWorkersAI",
                FallbackProvider = "OpenRouter",
                FallbackEnabled = fallbackEnabled,
                FallbackCooldownSeconds = 60,
                RequestTimeoutSeconds = 180,
                ExtractionTimeoutSeconds = 600
            }),
            TimeProvider.System,
            logger ?? NullLogger<LlmChatService>.Instance);
    }

    private static async Task<List<string>> CollectAsync(IAsyncEnumerable<string> source)
    {
        var result = new List<string>();
        await foreach (var item in source)
        {
            result.Add(item);
        }

        return result;
    }

    private sealed class FakeChatClient : IChatClient
    {
        public string ResponseText { get; set; } = "complete response";
        public ChatResponse? Response { get; set; }
        public Exception? CompleteException { get; set; }
        public IReadOnlyList<string> StreamTexts { get; set; } = [];
        public Exception? StreamExceptionBeforeFirst { get; set; }
        public Exception? StreamExceptionAfterText { get; set; }
        public List<ChatOptions> Options { get; } = [];
        public int StreamingCalls { get; private set; }

        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Options.Add(options?.Clone() ?? new ChatOptions());
            if (CompleteException is not null)
            {
                return Task.FromException<ChatResponse>(CompleteException);
            }

            return Task.FromResult(Response ?? new ChatResponse(
                new ChatMessage(ChatRole.Assistant, ResponseText)));
        }

        public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            StreamingCalls++;
            Options.Add(options?.Clone() ?? new ChatOptions());
            await Task.Yield();
            cancellationToken.ThrowIfCancellationRequested();
            if (StreamExceptionBeforeFirst is not null)
            {
                throw StreamExceptionBeforeFirst;
            }

            foreach (var text in StreamTexts)
            {
                yield return new ChatResponseUpdate(ChatRole.Assistant, text);
            }

            if (StreamExceptionAfterText is not null)
            {
                throw StreamExceptionAfterText;
            }
        }

        public object? GetService(Type serviceType, object? serviceKey = null) => null;

        public void Dispose()
        {
        }
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var values = state is IEnumerable<KeyValuePair<string, object?>> properties
                ? properties.ToDictionary(item => item.Key, item => item.Value)
                : [];
            Entries.Add(new LogEntry(logLevel, formatter(state, exception), values, exception));
        }
    }

    private sealed record LogEntry(
        LogLevel Level,
        string Message,
        IReadOnlyDictionary<string, object?> Values,
        Exception? Exception);
}
