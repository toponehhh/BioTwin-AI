using System.Net;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using BioTwin_AI.AspNetCoreApi.Application.Reranking;
using BioTwin_AI.AspNetCoreApi.Infrastructure.Ai;
using Microsoft.Extensions.Options;

namespace BioTwin_AI.AspNetCoreApi.Tests.Application;

public sealed class CloudflareRerankServiceTests
{
    [Fact]
    public async Task RerankAsync_posts_native_request_and_maps_ranked_indices()
    {
        var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(
                """
                {"success":true,"result":{"response":[{"id":1,"score":0.91},{"id":0,"score":0.32}]}}
                """,
                Encoding.UTF8,
                "application/json")
        });
        var factory = new FakeHttpClientFactory(new HttpClient(handler));
        var service = CreateService(factory);

        var results = await service.RerankAsync(
            "Which experience is relevant?",
            ["Built an API.", "Built an AI search system."],
            2,
            CancellationToken.None);

        Assert.Equal([new RerankResult(1, 0.91), new RerankResult(0, 0.32)], results);
        Assert.Equal(AiProviderNames.CloudflareAi, factory.ClientName);
        Assert.Equal(
            "https://api.cloudflare.com/client/v4/accounts/account/ai/run/@cf/baai/bge-reranker-base",
            handler.Request!.RequestUri!.AbsoluteUri);
        using var payload = JsonDocument.Parse(handler.Body!);
        Assert.Equal(2, payload.RootElement.GetProperty("top_k").GetInt32());
        Assert.Equal(2, payload.RootElement.GetProperty("contexts").GetArrayLength());
    }

    [Theory]
    [InlineData("{\"success\":false,\"result\":{\"response\":[{\"id\":0,\"score\":0.5}]}}")]
    [InlineData("{\"success\":true,\"result\":{\"response\":[{\"id\":2,\"score\":0.5}]}}")]
    [InlineData("{\"success\":true,\"result\":{\"response\":[{\"id\":0,\"score\":0.5},{\"id\":0,\"score\":0.4}]}}")]
    [InlineData("{\"success\":true,\"result\":{\"response\":[]}}")]
    public async Task RerankAsync_rejects_invalid_response_entries(string json)
    {
        var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        });
        var service = CreateService(new FakeHttpClientFactory(new HttpClient(handler)));

        await Assert.ThrowsAsync<RerankResponseException>(() => service.RerankAsync(
            "query",
            ["first", "second"],
            2,
            CancellationToken.None));
    }

    [Theory]
    [InlineData("{")]
    [InlineData("{\"success\":true,\"result\":{\"response\":[{\"id\":0,\"score\":1e400}]}}")]
    public async Task RerankAsync_rejects_malformed_or_non_finite_responses(string json)
    {
        var handler = new RecordingHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        });
        var service = CreateService(new FakeHttpClientFactory(new HttpClient(handler)));

        await Assert.ThrowsAsync<RerankResponseException>(() => service.RerankAsync(
            "query",
            ["first"],
            1,
            CancellationToken.None));
    }

    [Fact]
    public async Task RerankAsync_propagates_in_flight_caller_cancellation()
    {
        var service = CreateService(new FakeHttpClientFactory(new HttpClient(new BlockingHandler())));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.RerankAsync(
            "query",
            ["first"],
            1,
            cancellation.Token));
    }

    private static CloudflareRerankService CreateService(IHttpClientFactory factory)
    {
        return new CloudflareRerankService(
            factory,
            Options.Create(new CloudflareAiOptions
            {
                AccountId = "account",
                ApiToken = "token",
                RerankModel = "@cf/baai/bge-reranker-base"
            }),
            Options.Create(new RerankFailoverOptions { RequestTimeoutSeconds = 60 }));
    }

    private sealed class FakeHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public string? ClientName { get; private set; }

        public HttpClient CreateClient(string name)
        {
            ClientName = name;
            return client;
        }
    }

    private sealed class RecordingHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        public string? Body { get; private set; }

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
