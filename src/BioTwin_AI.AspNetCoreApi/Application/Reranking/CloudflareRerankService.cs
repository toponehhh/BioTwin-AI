using System.Net.Http.Json;
using System.Text.Json;
using BioTwin_AI.AspNetCoreApi.Infrastructure.Ai;
using Microsoft.Extensions.Options;

namespace BioTwin_AI.AspNetCoreApi.Application.Reranking;

public sealed class CloudflareRerankService(
    IHttpClientFactory httpClientFactory,
    IOptions<CloudflareAiOptions> cloudflareOptions,
    IOptions<RerankFailoverOptions> rerankOptions) : IRerankService
{
    private readonly CloudflareAiOptions _cloudflare = cloudflareOptions.Value;
    private readonly RerankFailoverOptions _rerank = rerankOptions.Value;

    public async Task<IReadOnlyList<RerankResult>> RerankAsync(
        string query,
        IReadOnlyList<string> documents,
        int limit,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(query);
        if (documents.Count == 0 || limit <= 0)
        {
            return [];
        }

        cancellationToken.ThrowIfCancellationRequested();
        if (!_cloudflare.IsConfigured)
        {
            throw new RerankResponseException("Cloudflare reranking is not configured.");
        }

        var topK = Math.Min(limit, documents.Count);
        var client = httpClientFactory.CreateClient(AiProviderNames.CloudflareAi);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, _rerank.RequestTimeoutSeconds)));
        var contexts = documents.Select(document => new { text = document ?? string.Empty }).ToArray();
        using var response = await client.PostAsJsonAsync(
            _cloudflare.GetNativeModelUri(_cloudflare.RerankModel),
            new { query, contexts, top_k = topK },
            timeout.Token);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException("Cloudflare rerank request failed.", null, response.StatusCode);
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
            throw new RerankResponseException("Cloudflare reranking returned malformed JSON.");
        }

        using (document)
        {
            if (!TryGetResponseArray(document.RootElement, out var entries) ||
                entries.GetArrayLength() == 0 ||
                entries.GetArrayLength() > topK)
            {
                throw new RerankResponseException("Cloudflare reranking returned an invalid response shape.");
            }

            var results = new List<RerankResult>(entries.GetArrayLength());
            var seen = new HashSet<int>();
            foreach (var entry in entries.EnumerateArray())
            {
                if (entry.ValueKind != JsonValueKind.Object ||
                    !entry.TryGetProperty("id", out var idElement) ||
                    !entry.TryGetProperty("score", out var scoreElement) ||
                    !idElement.TryGetInt32(out var id))
                {
                    throw new RerankResponseException("Cloudflare reranking returned an invalid result entry.");
                }

                double score;
                try
                {
                    score = scoreElement.GetDouble();
                }
                catch (Exception ex) when (ex is InvalidOperationException or FormatException or OverflowException)
                {
                    throw new RerankResponseException("Cloudflare reranking returned a non-numeric score.");
                }

                if (id < 0 || id >= documents.Count || !seen.Add(id) || !double.IsFinite(score))
                {
                    throw new RerankResponseException("Cloudflare reranking returned an invalid result index or score.");
                }

                results.Add(new RerankResult(id, score));
            }

            return results;
        }
    }

    private static bool TryGetResponseArray(JsonElement root, out JsonElement entries)
    {
        entries = default;
        return root.ValueKind == JsonValueKind.Object &&
            root.TryGetProperty("success", out var success) &&
            success.ValueKind == JsonValueKind.True &&
            root.TryGetProperty("result", out var result) &&
            result.ValueKind == JsonValueKind.Object &&
            result.TryGetProperty("response", out entries) &&
            entries.ValueKind == JsonValueKind.Array;
    }
}
