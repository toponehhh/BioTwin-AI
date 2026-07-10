using BioTwin_AI.AspNetCoreApi.Infrastructure.Ai;
using BioTwin_AI.AspNetCoreApi.Application.Llm;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text;
using System.Text.Json;

namespace BioTwin_AI.AspNetCoreApi.Tests.Infrastructure;

public sealed class AiProviderConfigurationTests
{
    [Fact]
    public void Cloudflare_openai_base_uri_uses_account_ai_v1_path()
    {
        var options = new CloudflareAiOptions { AccountId = "account-123" };

        Assert.Equal(
            "https://api.cloudflare.com/client/v4/accounts/account-123/ai/v1/",
            options.OpenAiBaseUri.AbsoluteUri);
    }

    [Fact]
    public void Cloudflare_native_model_uri_preserves_the_model_path()
    {
        var options = new CloudflareAiOptions { AccountId = "account-123" };

        var uri = options.GetNativeModelUri("@cf/baai/bge-reranker-base");

        Assert.Equal(
            "https://api.cloudflare.com/client/v4/accounts/account-123/ai/run/@cf/baai/bge-reranker-base",
            uri.AbsoluteUri);
    }

    [Theory]
    [InlineData("@cf/baai/..")]
    [InlineData("@cf/baai/bge/model")]
    public void Cloudflare_native_model_uri_rejects_unsafe_or_extra_segments(string model)
    {
        var options = new CloudflareAiOptions { AccountId = "account-123" };

        Assert.Throws<ArgumentException>(() => options.GetNativeModelUri(model));
    }

    [Fact]
    public void Missing_cloudflare_token_leaves_the_provider_unconfigured_without_startup_failure()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CloudflareAI:AccountId"] = "account-123",
                ["CloudflareAI:ApiToken"] = "",
                ["LLM:RequestTimeoutSeconds"] = "180",
                ["LLM:ExtractionTimeoutSeconds"] = "600"
            })
            .Build();
        var services = new ServiceCollection();

        services.AddAiProviders(configuration);

        using var provider = services.BuildServiceProvider(validateScopes: true);
        var options = provider.GetRequiredService<IOptions<CloudflareAiOptions>>().Value;
        var client = provider.GetRequiredService<IHttpClientFactory>().CreateClient(AiProviderNames.CloudflareAi);

        Assert.False(options.IsConfigured);
        Assert.Null(client.DefaultRequestHeaders.Authorization);
    }

    [Fact]
    public void ProviderCooldownState_expires_using_TimeProvider()
    {
        var clock = new ManualTimeProvider(DateTimeOffset.Parse("2026-07-10T00:00:00Z"));
        var state = new ProviderCooldownState(clock, TimeSpan.FromSeconds(60));

        state.MarkRetryableFailure();

        Assert.True(state.IsCoolingDown);
        clock.Advance(TimeSpan.FromSeconds(61));
        Assert.False(state.IsCoolingDown);
    }

    [Fact]
    public void Cloudflare_LLM_registration_uses_the_native_CloudflareChatClient()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CloudflareAI:AccountId"] = "account-123",
                ["CloudflareAI:ApiToken"] = "token",
                ["CloudflareAI:ChatModel"] = "@cf/test/chat",
                ["CloudflareAI:ExtractionModel"] = "@cf/test/extraction",
                ["OpenRouter:ApiKey"] = "fallback-token"
            })
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAiProviders(configuration);

        using var provider = services.BuildServiceProvider(validateScopes: true);
        var clients = provider.GetRequiredService<LlmProviderClients>();

        Assert.IsType<CloudflareChatClient>(clients.Cloudflare.Client);
        Assert.NotNull(clients.OpenRouter.Client);
        Assert.IsNotType<CloudflareChatClient>(clients.OpenRouter.Client);
    }

    [Fact]
    public async Task Cloudflare_HTTP_transport_does_not_log_account_or_request_content()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CloudflareAI:AccountId"] = "private-account-id",
                ["CloudflareAI:ApiToken"] = "private-token",
                ["CloudflareAI:ChatModel"] = "@cf/test/chat"
            })
            .Build();
        var logProvider = new RecordingLoggerProvider();
        var services = new ServiceCollection();
        services.AddLogging(builder => builder
            .SetMinimumLevel(LogLevel.Trace)
            .AddProvider(logProvider));
        services.AddAiProviders(configuration);
        services.AddHttpClient(AiProviderNames.CloudflareAi)
            .ConfigurePrimaryHttpMessageHandler(() => new StaticResponseHandler());

        await using var provider = services.BuildServiceProvider(validateScopes: true);
        var clients = provider.GetRequiredService<LlmProviderClients>();
        await clients.Cloudflare.Client!.GetResponseAsync(
            [new Microsoft.Extensions.AI.ChatMessage(Microsoft.Extensions.AI.ChatRole.User, "private prompt")],
            new Microsoft.Extensions.AI.ChatOptions { ModelId = "@cf/test/chat" });

        Assert.DoesNotContain(logProvider.Messages, message =>
            message.Contains("private-account-id", StringComparison.Ordinal) ||
            message.Contains("private-token", StringComparison.Ordinal) ||
            message.Contains("private prompt", StringComparison.Ordinal));
    }

    [Fact]
    public void Appsettings_use_Cloudflare_first_defaults_without_secrets()
    {
        var root = FindSolutionRoot();
        using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(
            root,
            "src",
            "BioTwin_AI.AspNetCoreApi",
            "appsettings.json")));
        var config = document.RootElement;

        Assert.Equal("CloudflareWorkersAI", config.GetProperty("LLM").GetProperty("PrimaryProvider").GetString());
        Assert.Equal("OpenRouter", config.GetProperty("LLM").GetProperty("FallbackProvider").GetString());
        Assert.Equal("", config.GetProperty("CloudflareAI").GetProperty("AccountId").GetString());
        Assert.Equal("", config.GetProperty("CloudflareAI").GetProperty("ApiToken").GetString());
        Assert.Equal("@cf/baai/bge-m3", config.GetProperty("CloudflareAI").GetProperty("EmbeddingModel").GetString());
        Assert.Equal("@cf/baai/bge-reranker-base", config.GetProperty("CloudflareAI").GetProperty("RerankModel").GetString());
        Assert.Equal("BgeM3Onnx", config.GetProperty("Embedding").GetProperty("FallbackProvider").GetString());
        Assert.Equal("BgeRerankerOnnx", config.GetProperty("Rerank").GetProperty("FallbackProvider").GetString());
        Assert.Equal(20, config.GetProperty("Rerank").GetProperty("CandidateCount").GetInt32());
    }

    private static string FindSolutionRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "BioTwin_AI.slnx")))
            {
                return current.FullName;
            }

            current = current.Parent;
        }

        throw new DirectoryNotFoundException("BioTwin_AI.slnx was not found.");
    }

    private sealed class ManualTimeProvider(DateTimeOffset current) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => current;

        public void Advance(TimeSpan duration) => current += duration;
    }

    private sealed class StaticResponseHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "{\"choices\":[{\"message\":{\"content\":\"ok\"},\"finish_reason\":\"stop\"}]}",
                    Encoding.UTF8,
                    "application/json")
            });
        }
    }

    private sealed class RecordingLoggerProvider : ILoggerProvider
    {
        public List<string> Messages { get; } = [];

        public ILogger CreateLogger(string categoryName) => new RecordingLogger(Messages);

        public void Dispose()
        {
        }
    }

    private sealed class RecordingLogger(List<string> messages) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            messages.Add(formatter(state, exception));
        }
    }
}
