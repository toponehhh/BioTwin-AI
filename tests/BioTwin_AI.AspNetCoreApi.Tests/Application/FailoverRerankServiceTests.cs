using System.Net;
using BioTwin_AI.AspNetCoreApi.Application.Reranking;
using BioTwin_AI.AspNetCoreApi.Infrastructure.Ai;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BioTwin_AI.AspNetCoreApi.Tests.Application;

public sealed class FailoverRerankServiceTests
{
    [Fact]
    public async Task RerankAsync_does_not_create_local_model_when_Cloudflare_succeeds()
    {
        var primary = new FakeRerankService([new RerankResult(1, 0.9)]);
        var local = new FakeLocalFactory(new FakeRerankService([new RerankResult(0, 0.5)]));
        var service = CreateService(primary, local);

        var results = await service.RerankAsync("query", ["first", "second"], 1);

        Assert.Equal(1, Assert.Single(results).Index);
        Assert.Equal(0, local.CreateCalls);
    }

    [Fact]
    public async Task RerankAsync_uses_local_model_after_retryable_failure()
    {
        var primary = new FakeRerankService(
            new HttpRequestException("rate limited", null, HttpStatusCode.TooManyRequests));
        var local = new FakeLocalFactory(new FakeRerankService([new RerankResult(0, 0.8)]));
        var service = CreateService(primary, local);

        var results = await service.RerankAsync("query", ["first", "second"], 1);

        Assert.Equal(0, Assert.Single(results).Index);
        Assert.Equal(1, local.CreateCalls);
    }

    [Fact]
    public async Task RerankAsync_uses_local_model_when_Cloudflare_rejects_model_request()
    {
        var primary = new FakeRerankService(
            new HttpRequestException("unsupported model request", null, HttpStatusCode.BadRequest));
        var local = new FakeLocalFactory(new FakeRerankService([new RerankResult(0, 0.8)]));
        var service = CreateService(primary, local);

        var results = await service.RerankAsync("query", ["document"], 1);

        Assert.Equal(0, Assert.Single(results).Index);
        Assert.Equal(1, local.CreateCalls);
    }

    [Fact]
    public async Task RerankAsync_skips_primary_during_cooldown()
    {
        var primary = new MutableRerankService
        {
            Exception = new HttpRequestException("unavailable", null, HttpStatusCode.ServiceUnavailable)
        };
        var local = new FakeLocalFactory(new FakeRerankService([new RerankResult(0, 0.8)]));
        var service = CreateService(primary, local);

        await service.RerankAsync("first", ["document"], 1);
        primary.Exception = null;
        primary.Results = [new RerankResult(0, 0.9)];
        await service.RerankAsync("second", ["document"], 1);

        Assert.Equal(1, primary.Calls);
        Assert.Equal(2, local.CreateCalls);
    }

    [Fact]
    public async Task RerankAsync_logs_provider_and_model_without_attaching_exception()
    {
        var logger = new RecordingLogger<FailoverRerankService>();
        var primary = new FakeRerankService(
            new HttpRequestException("private response", null, HttpStatusCode.ServiceUnavailable));
        var local = new FakeLocalFactory(new FakeRerankService([new RerankResult(0, 0.8)]));
        var service = CreateService(primary, local, logger);

        await service.RerankAsync("query", ["document"], 1);

        var entry = Assert.Single(logger.Entries);
        Assert.Equal("@cf/baai/bge-reranker-base", entry.Values["Model"]);
        Assert.Null(entry.Exception);
    }

    [Fact]
    public async Task RerankAsync_returns_empty_results_when_both_providers_fail()
    {
        var primary = new FakeRerankService(
            new HttpRequestException("private remote detail", null, HttpStatusCode.ServiceUnavailable));
        var local = new FakeLocalFactory(
            new FakeRerankService(new InvalidOperationException("private local detail")));
        var service = CreateService(primary, local);

        var results = await service.RerankAsync("query", ["first", "second"], 2);

        Assert.Empty(results);
    }

    [Fact]
    public async Task RerankAsync_does_not_fall_back_after_caller_cancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var local = new FakeLocalFactory(new FakeRerankService([]));
        var service = CreateService(new FakeRerankService([]), local);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.RerankAsync(
            "query",
            ["first"],
            1,
            cancellation.Token));

        Assert.Equal(0, local.CreateCalls);
    }

    private static FailoverRerankService CreateService(
        IRerankService primary,
        ILocalRerankServiceFactory local,
        ILogger<FailoverRerankService>? logger = null)
    {
        return new FailoverRerankService(
            primary,
            local,
            Options.Create(new RerankFailoverOptions
            {
                FallbackEnabled = true,
                FallbackCooldownSeconds = 60,
                RequestTimeoutSeconds = 60
            }),
            Options.Create(new CloudflareAiOptions
            {
                RerankModel = "@cf/baai/bge-reranker-base"
            }),
            TimeProvider.System,
            logger ?? NullLogger<FailoverRerankService>.Instance);
    }

    private sealed class FakeLocalFactory(IRerankService service) : ILocalRerankServiceFactory
    {
        public int CreateCalls { get; private set; }

        public IRerankService GetOrCreate()
        {
            CreateCalls++;
            return service;
        }
    }

    private sealed class FakeRerankService : IRerankService
    {
        private readonly IReadOnlyList<RerankResult>? results;
        private readonly Exception? exception;

        public FakeRerankService(IReadOnlyList<RerankResult> results) => this.results = results;
        public FakeRerankService(Exception exception) => this.exception = exception;

        public Task<IReadOnlyList<RerankResult>> RerankAsync(
            string query,
            IReadOnlyList<string> documents,
            int limit,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return exception is null
                ? Task.FromResult(results!)
                : Task.FromException<IReadOnlyList<RerankResult>>(exception);
        }
    }

    private sealed class MutableRerankService : IRerankService
    {
        public IReadOnlyList<RerankResult> Results { get; set; } = [];
        public Exception? Exception { get; set; }
        public int Calls { get; private set; }

        public Task<IReadOnlyList<RerankResult>> RerankAsync(
            string query,
            IReadOnlyList<string> documents,
            int limit,
            CancellationToken cancellationToken = default)
        {
            Calls++;
            return Exception is null
                ? Task.FromResult(Results)
                : Task.FromException<IReadOnlyList<RerankResult>>(Exception);
        }
    }

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var values = state is IEnumerable<KeyValuePair<string, object?>> properties
                ? properties.ToDictionary(item => item.Key, item => item.Value)
                : [];
            Entries.Add(new LogEntry(values, exception));
        }
    }

    private sealed record LogEntry(IReadOnlyDictionary<string, object?> Values, Exception? Exception);
}
