using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using BioTwin_AI.AspNetCoreApi.Infrastructure.Ai;
using Microsoft.Extensions.AI;

namespace BioTwin_AI.AspNetCoreApi.Application.Llm;

public sealed class CloudflareChatClient(
    IHttpClientFactory httpClientFactory) : IChatClient
{
    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var requestOptions = options ?? new ChatOptions();
        var model = RequireModel(requestOptions);
        using var request = CreateRequest(messages, requestOptions, model, stream: false);
        var client = httpClientFactory.CreateClient(AiProviderNames.CloudflareAi);
        using var response = await client.SendAsync(request, cancellationToken);
        EnsureSuccess(response);

        using var document = await ParseDocumentAsync(response, cancellationToken);
        var choice = GetFirstChoice(document.RootElement);
        if (!choice.TryGetProperty("message", out var message) ||
            message.ValueKind != JsonValueKind.Object ||
            !message.TryGetProperty("content", out var content))
        {
            throw InvalidResponse("Cloudflare chat returned an invalid message shape.");
        }

        var result = new ChatResponse(new ChatMessage(
            ChatRole.Assistant,
            ReadContent(content)))
        {
            ResponseId = ReadOptionalString(document.RootElement, "id"),
            ModelId = ReadOptionalString(document.RootElement, "model") ?? model,
            FinishReason = ReadFinishReason(choice),
            Usage = ReadUsage(document.RootElement)
        };
        return result;
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var requestOptions = options ?? new ChatOptions();
        var model = RequireModel(requestOptions);
        using var request = CreateRequest(messages, requestOptions, model, stream: true);
        var client = httpClientFactory.CreateClient(AiProviderNames.CloudflareAi);
        using var response = await client.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        EnsureSuccess(response);

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);
        var receivedDone = false;
        while (await reader.ReadLineAsync(cancellationToken) is { } line)
        {
            if (!line.StartsWith("data:", StringComparison.Ordinal))
            {
                continue;
            }

            var data = line[5..].TrimStart();
            if (data.Length == 0)
            {
                continue;
            }

            if (string.Equals(data, "[DONE]", StringComparison.Ordinal))
            {
                receivedDone = true;
                break;
            }

            JsonDocument document;
            try
            {
                document = JsonDocument.Parse(data);
            }
            catch (JsonException)
            {
                throw InvalidResponse("Cloudflare chat stream returned malformed JSON.");
            }

            using (document)
            {
                var choice = GetFirstChoice(document.RootElement);
                var text = string.Empty;
                if (choice.TryGetProperty("delta", out var delta) &&
                    delta.ValueKind == JsonValueKind.Object &&
                    delta.TryGetProperty("content", out var content) &&
                    content.ValueKind is not JsonValueKind.Null)
                {
                    text = ReadContent(content);
                }

                yield return new ChatResponseUpdate(ChatRole.Assistant, text)
                {
                    ResponseId = ReadOptionalString(document.RootElement, "id"),
                    ModelId = ReadOptionalString(document.RootElement, "model") ?? model,
                    FinishReason = ReadFinishReason(choice)
                };
            }
        }

        if (!receivedDone)
        {
            throw InvalidResponse("Cloudflare chat stream ended before the completion marker.");
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null)
    {
        return serviceKey is null &&
            (serviceType == typeof(CloudflareChatClient) || serviceType == typeof(IChatClient))
                ? this
                : null;
    }

    public void Dispose()
    {
    }

    private static HttpRequestMessage CreateRequest(
        IEnumerable<ChatMessage> messages,
        ChatOptions options,
        string model,
        bool stream)
    {
        var payload = new ChatCompletionRequest(
            model,
            messages.Select(message => new RequestMessage(message.Role.Value, message.Text)).ToArray(),
            stream,
            options.Temperature,
            options.MaxOutputTokens,
            CreateResponseFormat(options.ResponseFormat));
        return new HttpRequestMessage(HttpMethod.Post, "chat/completions")
        {
            Content = JsonContent.Create(payload)
        };
    }

    private static object? CreateResponseFormat(ChatResponseFormat? responseFormat)
    {
        if (responseFormat is not ChatResponseFormatJson jsonFormat)
        {
            return null;
        }

        if (jsonFormat.Schema is not { } schema)
        {
            return new { type = "json_object" };
        }

        return new
        {
            type = "json_schema",
            json_schema = new
            {
                name = jsonFormat.SchemaName ?? "json_schema",
                description = jsonFormat.SchemaDescription,
                strict = true,
                schema
            }
        };
    }

    private static string RequireModel(ChatOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.ModelId))
        {
            throw new ArgumentException("A Cloudflare model ID is required.", nameof(options));
        }

        return options.ModelId;
    }

    private static async Task<JsonDocument> ParseDocumentAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            return await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(cancellationToken),
                cancellationToken: cancellationToken);
        }
        catch (JsonException)
        {
            throw InvalidResponse("Cloudflare chat returned malformed JSON.");
        }
    }

    private static JsonElement GetFirstChoice(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object ||
            !root.TryGetProperty("choices", out var choices) ||
            choices.ValueKind != JsonValueKind.Array ||
            choices.GetArrayLength() == 0 ||
            choices[0].ValueKind != JsonValueKind.Object)
        {
            throw InvalidResponse("Cloudflare chat returned an invalid choices shape.");
        }

        return choices[0];
    }

    private static string ReadContent(JsonElement content)
    {
        return content.ValueKind switch
        {
            JsonValueKind.String => content.GetString() ?? string.Empty,
            JsonValueKind.Object => content.GetRawText(),
            _ => throw InvalidResponse("Cloudflare chat returned unsupported content.")
        };
    }

    private static string? ReadOptionalString(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) &&
            property.ValueKind == JsonValueKind.String
                ? property.GetString()
                : null;
    }

    private static ChatFinishReason? ReadFinishReason(JsonElement choice)
    {
        var value = ReadOptionalString(choice, "finish_reason");
        return value switch
        {
            "stop" => ChatFinishReason.Stop,
            "length" => ChatFinishReason.Length,
            "tool_calls" => ChatFinishReason.ToolCalls,
            "content_filter" => ChatFinishReason.ContentFilter,
            _ => null
        };
    }

    private static UsageDetails? ReadUsage(JsonElement root)
    {
        if (!root.TryGetProperty("usage", out var usage) ||
            usage.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        return new UsageDetails
        {
            InputTokenCount = ReadOptionalInt64(usage, "prompt_tokens"),
            OutputTokenCount = ReadOptionalInt64(usage, "completion_tokens"),
            TotalTokenCount = ReadOptionalInt64(usage, "total_tokens")
        };
    }

    private static long? ReadOptionalInt64(JsonElement element, string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) &&
            property.TryGetInt64(out var value)
                ? value
                : null;
    }

    private static void EnsureSuccess(HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                "Cloudflare chat request failed.",
                null,
                response.StatusCode);
        }
    }

    private static LlmProviderResponseException InvalidResponse(string message) => new(message);

    private sealed record ChatCompletionRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("messages")] IReadOnlyList<RequestMessage> Messages,
        [property: JsonPropertyName("stream")] bool Stream,
        [property: JsonPropertyName("temperature")]
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] float? Temperature,
        [property: JsonPropertyName("max_tokens")]
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] int? MaxTokens,
        [property: JsonPropertyName("response_format")]
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] object? ResponseFormat);

    private sealed record RequestMessage(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string Content);
}
