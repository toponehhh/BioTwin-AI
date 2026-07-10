using System.Net;
using BioTwin_AI.BlazorClient.Services.Api;
using Microsoft.Extensions.Logging;

namespace BioTwin_AI.BlazorClient.Tests.Services;

public sealed class ApiClientBaseLoggingTests
{
    [Fact]
    public async Task Non_success_response_logs_method_path_and_status_once()
    {
        var logger = new RecordingLogger();
        var client = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.InternalServerError), logger);

        var exception = await Assert.ThrowsAsync<HttpRequestException>(
            () => client.GetAsync<string>("api/resumes/state?uid=secret#review"));

        Assert.NotNull(exception);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Contains("GET", entry.Message, StringComparison.Ordinal);
        Assert.Contains("/api/resumes/state", entry.Message, StringComparison.Ordinal);
        Assert.Contains("500", entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("uid=secret", entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("review", entry.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Transport_failure_logs_once()
    {
        var logger = new RecordingLogger();
        var client = CreateClient(_ => throw new HttpRequestException("Network unavailable"), logger);

        await Assert.ThrowsAsync<HttpRequestException>(
            () => client.SendRequestAsync(HttpMethod.Post, "api/resumes/import-jobs/wizard?operationId=secret"));

        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Contains("POST", entry.Message, StringComparison.Ordinal);
        Assert.Contains("/api/resumes/import-jobs/wizard", entry.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("operationId=secret", entry.Message, StringComparison.Ordinal);
        Assert.NotNull(entry.Exception);
    }

    [Fact]
    public async Task Explicit_cancellation_does_not_log()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var logger = new RecordingLogger();
        var client = CreateClient(
            _ => throw new OperationCanceledException(cancellation.Token),
            logger);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => client.SendRequestAsync(HttpMethod.Get, "api/resumes/export/pdf", cancellation.Token));

        Assert.Empty(logger.Entries);
    }

    [Fact]
    public async Task Download_and_upload_send_paths_share_failure_logging()
    {
        var logger = new RecordingLogger();
        var client = CreateClient(_ => new HttpResponseMessage(HttpStatusCode.BadGateway), logger);

        await Assert.ThrowsAsync<HttpRequestException>(
            () => client.GetBytesForTestAsync("api/resumes/1/export/pdf?token=secret"));
        await Assert.ThrowsAsync<HttpRequestException>(
            () => client.SendRequestAsync(HttpMethod.Post, "api/resumes/import-jobs/workspace?operationId=secret"));

        Assert.Equal(2, logger.Entries.Count);
        Assert.All(logger.Entries, entry =>
        {
            Assert.Equal(LogLevel.Warning, entry.Level);
            Assert.DoesNotContain("secret", entry.Message, StringComparison.Ordinal);
        });
    }

    private static TestApiClient CreateClient(
        Func<HttpRequestMessage, HttpResponseMessage> send,
        RecordingLogger logger)
    {
        var httpClient = new HttpClient(new DelegateHandler(send))
        {
            BaseAddress = new Uri("https://api.example/")
        };

        return new TestApiClient(httpClient, logger);
    }

    private sealed class TestApiClient(HttpClient httpClient, ILogger logger) : ApiClientBase(httpClient, logger)
    {
        public new Task<T> GetAsync<T>(string uri, CancellationToken cancellationToken = default) =>
            base.GetAsync<T>(uri, cancellationToken);

        public Task<byte[]> GetBytesForTestAsync(string uri, CancellationToken cancellationToken = default) =>
            base.GetBytesAsync(uri, cancellationToken);

        public async Task SendRequestAsync(HttpMethod method, string uri, CancellationToken cancellationToken = default)
        {
            using var request = CreateCredentialedRequest(method, uri);
            using var response = await SendLoggedAsync(request, cancellationToken);
            await EnsureSuccessAsync(response, cancellationToken);
        }
    }

    private sealed class DelegateHandler(Func<HttpRequestMessage, HttpResponseMessage> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(send(request));
    }

    private sealed class RecordingLogger : ILogger
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
            Entries.Add(new LogEntry(logLevel, formatter(state, exception), exception));
        }
    }

    private sealed record LogEntry(LogLevel Level, string Message, Exception? Exception);
}
