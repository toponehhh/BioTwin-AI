using System.Text.RegularExpressions;

namespace BioTwin_AI.AspNetCoreApi.Infrastructure.Ai;

public sealed partial class CloudflareAiOptions
{
    public const string SectionName = "CloudflareAI";

    public string AccountId { get; set; } = string.Empty;
    public string ApiToken { get; set; } = string.Empty;
    public string ChatModel { get; set; } = "@cf/meta/llama-3.1-8b-instruct-fast";
    public string ExtractionModel { get; set; } = "@cf/meta/llama-3.3-70b-instruct-fp8-fast";
    public string EmbeddingModel { get; set; } = "@cf/baai/bge-m3";
    public string RerankModel { get; set; } = "@cf/baai/bge-reranker-base";

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(AccountId) &&
        !string.IsNullOrWhiteSpace(ApiToken);

    public Uri OpenAiBaseUri
    {
        get
        {
            var accountId = RequireAccountId();
            return new Uri($"https://api.cloudflare.com/client/v4/accounts/{Uri.EscapeDataString(accountId)}/ai/v1/");
        }
    }

    public Uri GetNativeModelUri(string model)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        if (!ModelPathPattern().IsMatch(model))
        {
            throw new ArgumentException("Cloudflare model must use an @provider/owner/model path.", nameof(model));
        }

        var accountId = RequireAccountId();
        return new Uri($"https://api.cloudflare.com/client/v4/accounts/{Uri.EscapeDataString(accountId)}/ai/run/{model}");
    }

    private string RequireAccountId()
    {
        if (string.IsNullOrWhiteSpace(AccountId))
        {
            throw new InvalidOperationException("Cloudflare Account ID is not configured.");
        }

        return AccountId.Trim();
    }

    [GeneratedRegex("^@[A-Za-z0-9][A-Za-z0-9._-]*/[A-Za-z0-9][A-Za-z0-9._-]*/[A-Za-z0-9][A-Za-z0-9._-]*$", RegexOptions.CultureInvariant)]
    private static partial Regex ModelPathPattern();
}
