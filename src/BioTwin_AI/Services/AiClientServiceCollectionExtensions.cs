using Microsoft.Extensions.AI;
using OpenAI;
using OpenAI.Chat;
using System.ClientModel;

namespace BioTwin_AI.Services;

public static class AiClientServiceCollectionExtensions
{
    public static IServiceCollection AddBioTwinAiClients(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddChatClient(CreateChatClient);

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
        var apiKey = FirstNonBlank(
            configuration["OpenRouter:ApiKey"],
            configuration["LLM:ApiKey"],
            Environment.GetEnvironmentVariable("OPENROUTER_API_KEY"));

        return apiKey ?? "not-needed";
    }

    private static string? FirstNonBlank(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }
}

