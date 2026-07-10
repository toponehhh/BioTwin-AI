using System.Net.Http.Json;
using System.Text.Json;
using BioTwin_AI.AspNetCoreApi.Infrastructure.Ai;
using Microsoft.Extensions.Options;

namespace BioTwin_AI.AspNetCoreApi.Application.Embeddings;

public sealed class CloudflareEmbeddingService(
    IHttpClientFactory httpClientFactory,
    IOptions<CloudflareAiOptions> cloudflareOptions,
    IOptions<EmbeddingFailoverOptions> embeddingOptions) : IEmbeddingService
{
    private readonly CloudflareAiOptions _cloudflare = cloudflareOptions.Value;
    private readonly EmbeddingFailoverOptions _embedding = embeddingOptions.Value;

    public async Task<float[]> EmbedAsync(string text, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        cancellationToken.ThrowIfCancellationRequested();
        if (!_cloudflare.IsConfigured)
        {
            throw new EmbeddingResponseException("Cloudflare embedding is not configured.");
        }

        var client = httpClientFactory.CreateClient(AiProviderNames.CloudflareAi);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, _embedding.RequestTimeoutSeconds)));
        using var response = await client.PostAsJsonAsync(
            "embeddings",
            new { model = _cloudflare.EmbeddingModel, input = text },
            timeout.Token);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                "Cloudflare embedding request failed.",
                null,
                response.StatusCode);
        }

        JsonDocument document;
        try
        {
            document = await JsonDocument.ParseAsync(
                await response.Content.ReadAsStreamAsync(timeout.Token),
                cancellationToken: timeout.Token);
        }
        catch (JsonException)
        {
            throw new EmbeddingResponseException("Cloudflare embedding returned malformed JSON.");
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("data", out var data) ||
                data.ValueKind != JsonValueKind.Array ||
                data.GetArrayLength() != 1 ||
                data[0].ValueKind != JsonValueKind.Object ||
                !data[0].TryGetProperty("embedding", out var embedding) ||
                embedding.ValueKind != JsonValueKind.Array)
            {
                throw new EmbeddingResponseException("Cloudflare embedding returned an invalid response shape.");
            }

            var vector = new float[embedding.GetArrayLength()];
            try
            {
                var index = 0;
                foreach (var value in embedding.EnumerateArray())
                {
                    vector[index++] = value.GetSingle();
                }
            }
            catch (Exception ex) when (ex is InvalidOperationException or FormatException or OverflowException)
            {
                throw new EmbeddingResponseException("Cloudflare embedding returned a non-numeric vector.");
            }

            return EmbeddingVectorValidator.ValidateAndNormalize(
                vector,
                _embedding.ExpectedDimensions);
        }
    }
}
