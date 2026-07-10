using System.Net;
using BioTwin_AI.AspNetCoreApi.Application.Embeddings;
using BioTwin_AI.AspNetCoreApi.Infrastructure.Ai;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace BioTwin_AI.AspNetCoreApi.Tests.Application;

public sealed class FailoverEmbeddingServiceTests
{
    [Fact]
    public async Task EmbedAsync_does_not_create_local_model_when_Cloudflare_succeeds()
    {
        var local = new FakeLocalFactory(new FakeEmbeddingService([0.6f, 0.8f]));
        var service = CreateService(new FakeEmbeddingService([3f, 4f]), local);

        var vector = await service.EmbedAsync("resume", CancellationToken.None);

        Assert.Equal([0.6f, 0.8f], vector);
        Assert.Equal(0, local.CreateCalls);
    }

    [Fact]
    public async Task EmbedAsync_uses_local_model_after_retryable_Cloudflare_failure()
    {
        var localService = new FakeEmbeddingService([0f, 2f]);
        var local = new FakeLocalFactory(localService);
        var primary = new FakeEmbeddingService(
            new HttpRequestException("rate limited", null, HttpStatusCode.TooManyRequests));
        var service = CreateService(primary, local);

        var vector = await service.EmbedAsync("resume", CancellationToken.None);

        Assert.Equal([0f, 1f], vector);
        Assert.Equal(1, local.CreateCalls);
        Assert.Equal(1, localService.Calls);
    }

    [Fact]
    public async Task EmbedAsync_uses_local_model_when_Cloudflare_rejects_model_request()
    {
        var local = new FakeLocalFactory(new FakeEmbeddingService([0f, 1f]));
        var primary = new FakeEmbeddingService(
            new HttpRequestException("unsupported model request", null, HttpStatusCode.BadRequest));
        var service = CreateService(primary, local);

        var vector = await service.EmbedAsync("resume", CancellationToken.None);

        Assert.Equal([0f, 1f], vector);
        Assert.Equal(1, local.CreateCalls);
    }

    [Fact]
    public async Task EmbedAsync_uses_local_model_when_primary_vector_has_wrong_dimensions()
    {
        var local = new FakeLocalFactory(new FakeEmbeddingService([0f, 1f]));
        var service = CreateService(new FakeEmbeddingService([1f]), local);

        var vector = await service.EmbedAsync("resume", CancellationToken.None);

        Assert.Equal([0f, 1f], vector);
        Assert.Equal(1, local.CreateCalls);
    }

    [Fact]
    public async Task EmbedAsync_uses_local_model_when_primary_vector_is_not_finite()
    {
        var local = new FakeLocalFactory(new FakeEmbeddingService([0f, 1f]));
        var service = CreateService(new FakeEmbeddingService([float.NaN, 1f]), local);

        var vector = await service.EmbedAsync("resume", CancellationToken.None);

        Assert.Equal([0f, 1f], vector);
        Assert.Equal(1, local.CreateCalls);
    }

    [Fact]
    public async Task EmbedAsync_uses_local_model_after_primary_timeout()
    {
        var local = new FakeLocalFactory(new FakeEmbeddingService([0f, 1f]));
        var service = CreateService(
            new FakeEmbeddingService(new TaskCanceledException("provider timeout")),
            local);

        var vector = await service.EmbedAsync("resume", CancellationToken.None);

        Assert.Equal([0f, 1f], vector);
        Assert.Equal(1, local.CreateCalls);
    }

    [Fact]
    public void Validator_normalizes_large_finite_values_without_float_overflow()
    {
        var vector = EmbeddingVectorValidator.ValidateAndNormalize(
            [float.MaxValue, float.MaxValue],
            2);

        Assert.All(vector, value => Assert.InRange(value, 0.7070f, 0.7072f));
    }

    [Fact]
    public async Task EmbedAsync_skips_primary_during_cooldown()
    {
        var primary = new FakeEmbeddingService(
            new HttpRequestException("unavailable", null, HttpStatusCode.ServiceUnavailable));
        var local = new FakeLocalFactory(new FakeEmbeddingService([0f, 1f]));
        var service = CreateService(primary, local);

        await service.EmbedAsync("first", CancellationToken.None);
        primary.Exception = null;
        primary.Vector = [1f, 0f];
        await service.EmbedAsync("second", CancellationToken.None);

        Assert.Equal(1, primary.Calls);
        Assert.Equal(2, local.CreateCalls);
    }

    [Fact]
    public async Task EmbedAsync_does_not_fall_back_after_caller_cancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var local = new FakeLocalFactory(new FakeEmbeddingService([0f, 1f]));
        var service = CreateService(new FakeEmbeddingService([1f, 0f]), local);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.EmbedAsync("resume", cancellation.Token));

        Assert.Equal(0, local.CreateCalls);
    }

    [Fact]
    public async Task EmbedAsync_checks_cancellation_again_before_creating_local_model()
    {
        using var cancellation = new CancellationTokenSource();
        var local = new FakeLocalFactory(new FakeEmbeddingService([0f, 1f]));
        var service = CreateService(
            new CancelingFailureEmbeddingService(cancellation),
            local);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.EmbedAsync("resume", cancellation.Token));

        Assert.Equal(0, local.CreateCalls);
    }

    [Fact]
    public async Task EmbedAsync_returns_safe_error_when_both_providers_fail()
    {
        var local = new FakeLocalFactory(new FakeEmbeddingService(new InvalidOperationException("private local detail")));
        var primary = new FakeEmbeddingService(
            new HttpRequestException("private Cloudflare detail", null, HttpStatusCode.ServiceUnavailable));
        var service = CreateService(primary, local);

        var exception = await Assert.ThrowsAsync<EmbeddingProviderException>(() =>
            service.EmbedAsync("resume", CancellationToken.None));

        Assert.Null(exception.InnerException);
        Assert.DoesNotContain("private", exception.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    private static FailoverEmbeddingService CreateService(
        IEmbeddingService primary,
        ILocalEmbeddingServiceFactory local)
    {
        return new FailoverEmbeddingService(
            primary,
            local,
            Options.Create(new EmbeddingFailoverOptions
            {
                FallbackEnabled = true,
                FallbackCooldownSeconds = 60,
                RequestTimeoutSeconds = 60,
                ExpectedDimensions = 2
            }),
            TimeProvider.System,
            NullLogger<FailoverEmbeddingService>.Instance);
    }

    private sealed class FakeLocalFactory(IEmbeddingService service) : ILocalEmbeddingServiceFactory
    {
        public int CreateCalls { get; private set; }

        public IEmbeddingService GetOrCreate()
        {
            CreateCalls++;
            return service;
        }
    }

    private sealed class FakeEmbeddingService : IEmbeddingService
    {
        public float[]? Vector { get; set; }
        public Exception? Exception { get; set; }

        public FakeEmbeddingService(float[] vector) => Vector = vector;
        public FakeEmbeddingService(Exception exception) => Exception = exception;
        public int Calls { get; private set; }

        public Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            return Exception is null
                ? Task.FromResult(Vector!)
                : Task.FromException<float[]>(Exception);
        }
    }

    private sealed class CancelingFailureEmbeddingService(
        CancellationTokenSource cancellation) : IEmbeddingService
    {
        public Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken)
        {
            cancellation.Cancel();
            return Task.FromException<float[]>(new HttpRequestException(
                "unavailable",
                null,
                HttpStatusCode.ServiceUnavailable));
        }
    }
}
