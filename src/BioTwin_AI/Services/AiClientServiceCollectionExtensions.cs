using Microsoft.Extensions.AI;
using OpenAI;
using OpenAI.Chat;
using OpenAI.Embeddings;
using System.ClientModel;

namespace BioTwin_AI.Services;

public static class AiClientServiceCollectionExtensions
{
    private const string OpenRouterHttpClientName = "BioTwin_AI.OpenRouter";

    public static IServiceCollection AddBioTwinAiClients(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpClient(OpenRouterHttpClientName, client =>
        {
            client.BaseAddress = GetOpenAiCompatibleEndpoint(configuration);
            client.Timeout = TimeSpan.FromSeconds(configuration.GetValue("LLM:EmbeddingTimeoutSeconds", 300));
        });

        services.AddChatClient(CreateChatClient);
        services.AddEmbeddingGenerator<string, Embedding<float>>(CreateEmbeddingGenerator);

        return services;
    }

    private static IChatClient CreateChatClient(IServiceProvider services)
    {
        var configuration = services.GetRequiredService<IConfiguration>();
        var model = configuration["LLM:Model"] ?? "openrouter/free";
        var credential = new ApiKeyCredential(GetApiKey(configuration));
        var chatClient = new ChatClient(
            model,
            credential,
            new OpenAIClientOptions { Endpoint = GetOpenAiCompatibleEndpoint(configuration) });

        return chatClient.AsIChatClient();
    }

    private static IEmbeddingGenerator<string, Embedding<float>> CreateEmbeddingGenerator(IServiceProvider services)
    {
        var configuration = services.GetRequiredService<IConfiguration>();
        var embeddingModel = configuration["LLM:EmbeddingModel"] ?? "text-embedding-3-large";
        var credential = new ApiKeyCredential(GetApiKey(configuration));
        var embeddingClient = new EmbeddingClient(
            embeddingModel,
            credential,
            new OpenAIClientOptions { Endpoint = GetOpenAiCompatibleEndpoint(configuration) });

        return embeddingClient.AsIEmbeddingGenerator();
    }

    private static Uri GetOpenAiCompatibleEndpoint(IConfiguration configuration)
    {
        var configured = configuration["LLM:BaseUrl"];
        if (string.IsNullOrWhiteSpace(configured))
        {
            return new Uri("https://openrouter.ai/api/v1");
        }

        var trimmed = configured.TrimEnd('/');
        if (!trimmed.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
        {
            trimmed += "/v1";
        }

        return new Uri(trimmed);
    }

    private static string GetApiKey(IConfiguration configuration)
    {
        var apiKey = configuration["LLM:ApiKey"];
        return string.IsNullOrWhiteSpace(apiKey) ? "not-needed" : apiKey;
    }
}

