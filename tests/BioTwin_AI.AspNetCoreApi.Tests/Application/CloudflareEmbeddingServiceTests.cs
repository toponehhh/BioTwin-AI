using System.Net;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using BioTwin_AI.AspNetCoreApi.Application.Embeddings;
using BioTwin_AI.AspNetCoreApi.Infrastructure.Ai;
using Microsoft.Extensions.Options;

namespace BioTwin_AI.AspNetCoreApi.Tests.Application;

public sealed class CloudflareEmbeddingServiceTests
{
    [Fact]
    public async Task EmbedAsync_posts_OpenAI_compatible_request_and_normalizes_vector()
    {
        var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """
                {"object":"list","data":[{"object":"embedding","index":0,"embedding":[3,4]}],"model":"@cf/baai/bge-m3"}
                """,
                Encoding.UTF8,
                "application/json")
        });
        var service = CreateService(handler);

        var vector = await service.EmbedAsync("private resume text", CancellationToken.None);

        Assert.Equal([0.6f, 0.8f], vector);
        Assert.Equal(HttpMethod.Post, handler.Request!.Method);
        Assert.Equal("https://api.cloudflare.test/embeddings", handler.Request.RequestUri!.AbsoluteUri);
        Assert.Equal(AiProviderNames.CloudflareAi, handler.ClientName);
        using var payload = JsonDocument.Parse(handler.Body!);
        Assert.Equal("@cf/baai/bge-m3", payload.RootElement.GetProperty("model").GetString());
        Assert.Equal("private resume text", payload.RootElement.GetProperty("input").GetString());
    }

    [Fact]
    public async Task EmbedAsync_rejects_an_empty_data_array()
    {
        var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"data\":[]}", Encoding.UTF8, "application/json")
        });
        var service = CreateService(handler);

        await Assert.ThrowsAsync<EmbeddingResponseException>(() =>
            service.EmbedAsync("resume", CancellationToken.None));
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("{\"data\":[null]}")]
    [InlineData("{\"data\":[1]}")]
    [InlineData("{\"data\":[{\"embedding\":[1]}]}")]
    [InlineData("{\"data\":[{\"embedding\":[0,0]}]}")]
    [InlineData("{\"data\":[{\"embedding\":[1,0]},{\"embedding\":[0,1]}]}")]
    [InlineData("{\"data\":[{\"embedding\":[1e400,1]}]}")]
    public async Task EmbedAsync_rejects_invalid_vectors(string json)
    {
        var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        });
        var service = CreateService(handler);

        await Assert.ThrowsAsync<EmbeddingResponseException>(() =>
            service.EmbedAsync("resume", CancellationToken.None));
    }

    [Fact]
    public async Task EmbedAsync_propagates_in_flight_caller_cancellation()
    {
        var handler = new BlockingHandler();
        var service = CreateService(handler);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            service.EmbedAsync("resume", cancellation.Token));
    }

    private static CloudflareEmbeddingService CreateService(HttpMessageHandler handler)
    {
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.cloudflare.test/") };
        return new CloudflareEmbeddingService(
            new FakeHttpClientFactory(
                client,
                name =>
                {
                    if (handler is RecordingHandler recordingHandler)
                    {
                        recordingHandler.ClientName = name;
                    }
                }),
            Options.Create(new CloudflareAiOptions
            {
                AccountId = "account",
                ApiToken = "token",
                EmbeddingModel = "@cf/baai/bge-m3"
            }),
            Options.Create(new EmbeddingFailoverOptions
            {
                ExpectedDimensions = 2,
                RequestTimeoutSeconds = 60
            }));
    }

    private sealed class FakeHttpClientFactory(
        HttpClient client,
        Action<string> onCreate) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            onCreate(name);
            return client;
        }
    }

    private sealed class RecordingHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public string? Body { get; private set; }
        public string? ClientName { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Request = request;
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return response;
        }
    }

    private sealed class BlockingHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new UnreachableException();
        }
    }
}
