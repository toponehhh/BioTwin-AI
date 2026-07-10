using System.Net.Http.Headers;
using BioTwin_AI.AspNetCoreApi.Application.Llm;
using BioTwin_AI.AspNetCoreApi.Application.Embeddings;
using BioTwin_AI.AspNetCoreApi.Application.Reranking;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Chat;
using System.ClientModel;

namespace BioTwin_AI.AspNetCoreApi.Infrastructure.Ai;

public static class AiServiceCollectionExtensions
{
    public static IServiceCollection AddAiProviders(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.TryAddSingleton(TimeProvider.System);

        services.AddOptions<CloudflareAiOptions>()
            .Bind(configuration.GetSection(CloudflareAiOptions.SectionName));
        services.AddOptions<LlmFailoverOptions>()
            .Bind(configuration.GetSection(LlmFailoverOptions.SectionName))
            .Validate(options => options.FallbackCooldownSeconds > 0, "LLM fallback cooldown must be positive.")
            .Validate(options => options.RequestTimeoutSeconds > 0, "LLM request timeout must be positive.")
            .Validate(options => options.ExtractionTimeoutSeconds > 0, "LLM extraction timeout must be positive.");
        services.AddOptions<EmbeddingFailoverOptions>()
            .Bind(configuration.GetSection(EmbeddingFailoverOptions.SectionName))
            .Validate(options => options.FallbackCooldownSeconds > 0, "Embedding fallback cooldown must be positive.")
            .Validate(options => options.RequestTimeoutSeconds > 0, "Embedding request timeout must be positive.")
            .Validate(options => options.ExpectedDimensions > 0, "Embedding dimensions must be positive.");
        services.AddOptions<RerankFailoverOptions>()
            .Bind(configuration.GetSection(RerankFailoverOptions.SectionName))
            .Validate(options => options.FallbackCooldownSeconds > 0, "Rerank fallback cooldown must be positive.")
            .Validate(options => options.RequestTimeoutSeconds > 0, "Rerank request timeout must be positive.")
            .Validate(options => options.CandidateCount > 0, "Rerank candidate count must be positive.");

        services.AddHttpClient(AiProviderNames.CloudflareAi, (serviceProvider, client) =>
        {
            var options = serviceProvider.GetRequiredService<IOptions<CloudflareAiOptions>>().Value;
            client.Timeout = Timeout.InfiniteTimeSpan;

            if (!string.IsNullOrWhiteSpace(options.AccountId))
            {
                client.BaseAddress = options.OpenAiBaseUri;
            }

            if (!string.IsNullOrWhiteSpace(options.ApiToken))
            {
                client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", options.ApiToken);
            }
        }).RemoveAllLoggers();

        services.AddHttpClient(AiProviderNames.OpenRouter, client =>
        {
            var baseUrl = configuration["OpenRouter:BaseUrl"]
                ?? configuration["LLM:BaseUrl"]
                ?? "https://openrouter.ai/api/v1";
            client.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
            client.Timeout = Timeout.InfiniteTimeSpan;

            var apiKey = FirstNonBlank(
                configuration["OpenRouter:ApiKey"],
                configuration["LLM:ApiKey"],
                Environment.GetEnvironmentVariable("OPENROUTER_API_KEY"));
            if (apiKey is not null)
            {
                client.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", apiKey);
            }
        });

        services.AddSingleton<CloudflareChatClient>();
        services.AddSingleton(serviceProvider =>
        {
            var cloudflare = serviceProvider.GetRequiredService<IOptions<CloudflareAiOptions>>().Value;
            var llm = serviceProvider.GetRequiredService<IOptions<LlmFailoverOptions>>().Value;
            var networkTimeout = TimeSpan.FromSeconds(Math.Max(
                llm.RequestTimeoutSeconds,
                llm.ExtractionTimeoutSeconds));

            IChatClient? cloudflareClient = cloudflare.IsConfigured
                ? serviceProvider.GetRequiredService<CloudflareChatClient>()
                : null;

            var openRouterApiKey = FirstNonBlank(
                configuration["OpenRouter:ApiKey"],
                configuration["LLM:ApiKey"],
                Environment.GetEnvironmentVariable("OPENROUTER_API_KEY"));
            var openRouterBaseUrl = configuration["OpenRouter:BaseUrl"]
                ?? configuration["LLM:BaseUrl"]
                ?? "https://openrouter.ai/api/v1";
            var openRouterChatModel = configuration["OpenRouter:ChatModel"]
                ?? configuration["LLM:Model"]
                ?? "openrouter/free";
            var openRouterExtractionModel = configuration["OpenRouter:ExtractionModel"]
                ?? configuration["LLM:ExtractionModel"]
                ?? "openai/gpt-oss-20b:free";
            IChatClient? openRouterClient = openRouterApiKey is null
                ? null
                : CreateChatClient(
                    NormalizeOpenAiEndpoint(openRouterBaseUrl),
                    openRouterApiKey,
                    openRouterChatModel,
                    networkTimeout);

            return new LlmProviderClients(
                new LlmProviderClient(
                    "CloudflareWorkersAI",
                    cloudflareClient,
                    cloudflare.ChatModel,
                    cloudflare.ExtractionModel),
                new LlmProviderClient(
                    "OpenRouter",
                    openRouterClient,
                    openRouterChatModel,
                    openRouterExtractionModel));
        });

        services.AddSingleton<CloudflareEmbeddingService>();
        services.AddSingleton<ILocalEmbeddingServiceFactory, LocalEmbeddingServiceFactory>();
        services.AddSingleton<IEmbeddingService>(serviceProvider =>
            new FailoverEmbeddingService(
                serviceProvider.GetRequiredService<CloudflareEmbeddingService>(),
                serviceProvider.GetRequiredService<ILocalEmbeddingServiceFactory>(),
                serviceProvider.GetRequiredService<IOptions<EmbeddingFailoverOptions>>(),
                serviceProvider.GetRequiredService<TimeProvider>(),
                serviceProvider.GetRequiredService<ILogger<FailoverEmbeddingService>>()));
        services.AddSingleton<CloudflareRerankService>();
        services.AddSingleton<ILocalRerankServiceFactory, LocalRerankServiceFactory>();
        services.AddSingleton<IRerankService>(serviceProvider =>
            new FailoverRerankService(
                serviceProvider.GetRequiredService<CloudflareRerankService>(),
                serviceProvider.GetRequiredService<ILocalRerankServiceFactory>(),
                serviceProvider.GetRequiredService<IOptions<RerankFailoverOptions>>(),
                serviceProvider.GetRequiredService<IOptions<CloudflareAiOptions>>(),
                serviceProvider.GetRequiredService<TimeProvider>(),
                serviceProvider.GetRequiredService<ILogger<FailoverRerankService>>()));

        return services;
    }

    private static IChatClient CreateChatClient(
        Uri endpoint,
        string apiKey,
        string model,
        TimeSpan networkTimeout)
    {
        var client = new ChatClient(
            model,
            new ApiKeyCredential(apiKey),
            new OpenAIClientOptions
            {
                Endpoint = endpoint,
                NetworkTimeout = networkTimeout
            });
        return client.AsIChatClient();
    }

    private static Uri NormalizeOpenAiEndpoint(string baseUrl)
    {
        var normalized = baseUrl.TrimEnd('/');
        if (!normalized.EndsWith("/v1", StringComparison.OrdinalIgnoreCase))
        {
            normalized += "/v1";
        }

        return new Uri(normalized);
    }

    private static string? FirstNonBlank(params string?[] values)
    {
        return values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));
    }
}
