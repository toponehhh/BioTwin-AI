using Microsoft.Extensions.AI;
using OllamaSharp;
using OpenAI;
using OpenAI.Chat;
using OpenAI.Embeddings;
using System.ClientModel;

namespace BioTwin_AI.Services;

public static class AiClientServiceCollectionExtensions
{
    private const string OllamaChatHttpClientName = "BioTwin_AI.Ollama.Chat";
    private const string OllamaEmbeddingHttpClientName = "BioTwin_AI.Ollama.Embedding";

    public static IServiceCollection AddBioTwinAiClients(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddHttpClient(OllamaChatHttpClientName, client =>
        {
            client.BaseAddress = GetOllamaEndpoint(configuration);
            client.Timeout = TimeSpan.FromSeconds(configuration.GetValue("LLM:ChatTimeoutSeconds", 300));
        });

        services.AddHttpClient(OllamaEmbeddingHttpClientName, client =>
        {
            client.BaseAddress = GetOllamaEndpoint(configuration);
            client.Timeout = TimeSpan.FromSeconds(configuration.GetValue("LLM:EmbeddingTimeoutSeconds", 300));
        });

        services.AddChatClient(CreateChatClient);
        services.AddEmbeddingGenerator<string, Embedding<float>>(CreateEmbeddingGenerator);

        return services;
    }

    private static IChatClient CreateChatClient(IServiceProvider services)
    {
        var configuration = services.GetRequiredService<IConfiguration>();
        var model = configuration["LLM:Model"] ?? "gemma4:e2b";

        if (IsOllama(configuration))
        {
            var httpClient = services.GetRequiredService<IHttpClientFactory>().CreateClient(OllamaChatHttpClientName);
            return new OllamaApiClient(httpClient, model);
        }

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
        var embeddingModel = configuration["LLM:EmbeddingModel"] ?? "nomic-embed-text";

        if (IsOllama(configuration))
        {
            var httpClient = services.GetRequiredService<IHttpClientFactory>().CreateClient(OllamaEmbeddingHttpClientName);
            return new OllamaApiClient(httpClient, embeddingModel);
        }

        var credential = new ApiKeyCredential(GetApiKey(configuration));
        var embeddingClient = new EmbeddingClient(
            embeddingModel,
            credential,
            new OpenAIClientOptions { Endpoint = GetOpenAiCompatibleEndpoint(configuration) });

        return embeddingClient.AsIEmbeddingGenerator();
    }

    private static bool IsOllama(IConfiguration configuration)
    {
        var provider = configuration["LLM:Provider"] ?? "Ollama";
        return string.Equals(provider, "Ollama", StringComparison.OrdinalIgnoreCase);
    }

    private static Uri GetOllamaEndpoint(IConfiguration configuration)
    {
        return new Uri(configuration["LLM:BaseUrl"] ?? "http://localhost:11434");
    }

    private static Uri GetOpenAiCompatibleEndpoint(IConfiguration configuration)
    {
        var configured = configuration["LLM:BaseUrl"];
        if (string.IsNullOrWhiteSpace(configured))
        {
            return new Uri("https://api.openai.com/v1");
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
