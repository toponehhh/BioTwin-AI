using System.Diagnostics;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using BioTwin_AI.AspNetCoreApi.Application.Llm;
using BioTwin_AI.AspNetCoreApi.Infrastructure.Ai;
using Microsoft.Extensions.AI;

namespace BioTwin_AI.AspNetCoreApi.Tests.Application;

public sealed class CloudflareChatClientTests
{
    [Fact]
    public async Task GetResponseAsync_serializes_object_content_as_JSON_text()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, "application/json", """
            {"id":"test-id","model":"@cf/test/model","choices":[{"message":{"role":"assistant","content":{"status":"ok","count":2}},"finish_reason":"stop"}],"usage":{"prompt_tokens":4,"completion_tokens":6,"total_tokens":10}}
            """);
        using var client = CreateClient(handler);

        var response = await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "synthetic input")],
            new ChatOptions
            {
                ModelId = "@cf/test/model",
                Temperature = 0,
                MaxOutputTokens = 64,
                ResponseFormat = ChatResponseFormat.ForJsonSchema<SyntheticResult>()
            },
            CancellationToken.None);

        using var document = JsonDocument.Parse(response.Text);
        Assert.Equal("ok", document.RootElement.GetProperty("status").GetString());
        Assert.Equal(2, document.RootElement.GetProperty("count").GetInt32());
        Assert.Equal("test-id", response.ResponseId);
        Assert.Equal("@cf/test/model", response.ModelId);
        Assert.Equal(ChatFinishReason.Stop, response.FinishReason);
        Assert.Equal(4, response.Usage!.InputTokenCount);
        Assert.Equal(6, response.Usage.OutputTokenCount);

        using var request = JsonDocument.Parse(handler.Body!);
        Assert.Equal("@cf/test/model", request.RootElement.GetProperty("model").GetString());
        Assert.False(request.RootElement.GetProperty("stream").GetBoolean());
        Assert.Equal(0, request.RootElement.GetProperty("temperature").GetSingle());
        Assert.Equal(64, request.RootElement.GetProperty("max_tokens").GetInt32());
        var requestMessage = request.RootElement.GetProperty("messages")[0];
        Assert.Equal("user", requestMessage.GetProperty("role").GetString());
        Assert.Equal("synthetic input", requestMessage.GetProperty("content").GetString());
        Assert.Equal("json_schema", request.RootElement.GetProperty("response_format").GetProperty("type").GetString());
        Assert.Equal(JsonValueKind.Object, request.RootElement.GetProperty("response_format").GetProperty("json_schema").GetProperty("schema").ValueKind);
    }

    [Fact]
    public async Task GetResponseAsync_accepts_string_content()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, "application/json", """
            {"choices":[{"message":{"content":"plain answer"},"finish_reason":"length"}]}
            """);
        using var client = CreateClient(handler);

        var response = await client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "synthetic input")],
            new ChatOptions { ModelId = "@cf/test/model" },
            CancellationToken.None);

        Assert.Equal("plain answer", response.Text);
        Assert.Equal(ChatFinishReason.Length, response.FinishReason);
    }

    [Fact]
    public async Task GetResponseAsync_preserves_HTTP_status_without_response_content()
    {
        var handler = new RecordingHandler(
            HttpStatusCode.TooManyRequests,
            "application/json",
            "private provider body");
        using var client = CreateClient(handler);

        var exception = await Assert.ThrowsAsync<HttpRequestException>(() => client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "private input")],
            new ChatOptions { ModelId = "@cf/test/model" },
            CancellationToken.None));

        Assert.Equal(HttpStatusCode.TooManyRequests, exception.StatusCode);
        Assert.DoesNotContain("private", exception.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetResponseAsync_rejects_malformed_JSON()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, "application/json", "not-json");
        using var client = CreateClient(handler);

        await Assert.ThrowsAsync<LlmProviderResponseException>(() => client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "synthetic input")],
            new ChatOptions { ModelId = "@cf/test/model" },
            CancellationToken.None));
    }

    [Fact]
    public async Task GetResponseAsync_propagates_caller_cancellation()
    {
        var handler = new BlockingHandler();
        using var client = CreateClient(handler);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.GetResponseAsync(
            [new ChatMessage(ChatRole.User, "synthetic input")],
            new ChatOptions { ModelId = "@cf/test/model" },
            cancellation.Token));
    }

    [Fact]
    public async Task GetStreamingResponseAsync_emits_SSE_text_and_stops_at_DONE()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, "text/event-stream", """
            data: {"id":"stream-id","model":"@cf/test/model","choices":[{"delta":{"role":"assistant","content":"Hello"},"finish_reason":null}]}

            data: {"id":"stream-id","model":"@cf/test/model","choices":[{"delta":{"content":" world"},"finish_reason":"stop"}]}

            data: [DONE]

            """);
        using var client = CreateClient(handler);

        var updates = await CollectAsync(client.GetStreamingResponseAsync(
            [new ChatMessage(ChatRole.User, "synthetic input")],
            new ChatOptions { ModelId = "@cf/test/model" },
            CancellationToken.None));

        Assert.Equal(["Hello", " world"], updates.Select(update => update.Text));
        Assert.Equal(ChatFinishReason.Stop, updates[^1].FinishReason);
        using var request = JsonDocument.Parse(handler.Body!);
        Assert.True(request.RootElement.GetProperty("stream").GetBoolean());
    }

    [Fact]
    public async Task GetStreamingResponseAsync_throws_on_malformed_event_after_text()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, "text/event-stream", """
            data: {"choices":[{"delta":{"content":"first"}}]}

            data: not-json

            """);
        using var client = CreateClient(handler);
        await using var enumerator = client.GetStreamingResponseAsync(
                [new ChatMessage(ChatRole.User, "synthetic input")],
                new ChatOptions { ModelId = "@cf/test/model" },
                CancellationToken.None)
            .GetAsyncEnumerator();

        Assert.True(await enumerator.MoveNextAsync());
        Assert.Equal("first", enumerator.Current.Text);
        await Assert.ThrowsAsync<LlmProviderResponseException>(async () =>
            await enumerator.MoveNextAsync().AsTask());
    }

    [Fact]
    public async Task GetStreamingResponseAsync_throws_on_EOF_without_DONE_after_text()
    {
        var handler = new RecordingHandler(HttpStatusCode.OK, "text/event-stream", """
            data: {"choices":[{"delta":{"content":"partial"}}]}

            """);
        using var client = CreateClient(handler);
        await using var enumerator = client.GetStreamingResponseAsync(
                [new ChatMessage(ChatRole.User, "synthetic input")],
                new ChatOptions { ModelId = "@cf/test/model" },
                CancellationToken.None)
            .GetAsyncEnumerator();

        Assert.True(await enumerator.MoveNextAsync());
        Assert.Equal("partial", enumerator.Current.Text);
        await Assert.ThrowsAsync<LlmProviderResponseException>(async () =>
            await enumerator.MoveNextAsync().AsTask());
    }

    [Fact]
    public async Task GetStreamingResponseAsync_propagates_caller_cancellation()
    {
        var handler = new BlockingHandler();
        using var client = CreateClient(handler);
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CollectAsync(
            client.GetStreamingResponseAsync(
                [new ChatMessage(ChatRole.User, "synthetic input")],
                new ChatOptions { ModelId = "@cf/test/model" },
                cancellation.Token)));
    }

    private static CloudflareChatClient CreateClient(HttpMessageHandler handler)
    {
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.cloudflare.test/")
        };
        return new CloudflareChatClient(new FakeHttpClientFactory(httpClient));
    }

    private static async Task<List<ChatResponseUpdate>> CollectAsync(
        IAsyncEnumerable<ChatResponseUpdate> source)
    {
        var updates = new List<ChatResponseUpdate>();
        await foreach (var update in source)
        {
            updates.Add(update);
        }

        return updates;
    }

    private sealed record SyntheticResult(string Status, int Count);

    private sealed class FakeHttpClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            Assert.Equal(AiProviderNames.CloudflareAi, name);
            return client;
        }
    }

    private sealed class RecordingHandler(
        HttpStatusCode statusCode,
        string mediaType,
        string responseBody) : HttpMessageHandler
    {
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, mediaType)
            };
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
