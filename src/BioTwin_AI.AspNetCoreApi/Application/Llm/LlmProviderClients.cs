using Microsoft.Extensions.AI;

namespace BioTwin_AI.AspNetCoreApi.Application.Llm;

public sealed record LlmProviderClient(
    string Name,
    IChatClient? Client,
    string ChatModel,
    string ExtractionModel)
{
    public bool IsConfigured => Client is not null;

    public string GetModel(LlmRequestKind requestKind)
    {
        return requestKind == LlmRequestKind.StructuredExtraction
            ? ExtractionModel
            : ChatModel;
    }
}

public sealed record LlmProviderClients(
    LlmProviderClient Cloudflare,
    LlmProviderClient OpenRouter)
{
    public LlmProviderClient Resolve(string? providerName)
    {
        if (string.Equals(providerName, "CloudflareWorkersAI", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(providerName, "Cloudflare", StringComparison.OrdinalIgnoreCase))
        {
            return Cloudflare;
        }

        if (string.Equals(providerName, "OpenRouter", StringComparison.OrdinalIgnoreCase))
        {
            return OpenRouter;
        }

        throw new InvalidOperationException($"Unknown LLM provider '{providerName}'.");
    }
}
