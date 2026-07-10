using System.Net;
using System.Text.Json;
using BioTwin_AI.BlazorClient.Services.Logging;
using BioTwin_AI.DotNetShared.Logging;
using Microsoft.Extensions.Logging;

namespace BioTwin_AI.BlazorClient.Tests.Logging;

public class RemoteClientLoggerTests
{
    [Fact]
    public async Task Sends_warning_with_path_but_without_query_string()
    {
        var handler = new RecordingHandler();
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.example/") };
        var logger = new RemoteClientLogger(
            client,
            "api/client-logs",
            "BioTwin_AI.BlazorClient.Pages.ResumeCreate",
            () => "https://client.example/resume/create?uid=secret#review",
            LogLevel.Warning);

        logger.LogWarning("Import failed for job {JobId}", "job-1");
        await handler.Received.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var entry = await handler.ReadEntryAsync();
        Assert.Equal("/resume/create", entry.Url);
        Assert.DoesNotContain("secret", handler.Body, StringComparison.Ordinal);
        Assert.DoesNotContain("review", handler.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Does_not_send_ordinary_information_or_http_infrastructure_categories()
    {
        var ordinaryHandler = new RecordingHandler();
        var ordinaryLogger = CreateLogger(
            ordinaryHandler,
            "BioTwin_AI.BlazorClient.Pages.ResumeCreate",
            LogLevel.Warning);

        ordinaryLogger.LogInformation("Routine client detail");
        await AssertNoRequestAsync(ordinaryHandler);

        var infrastructureHandler = new RecordingHandler();
        var infrastructureLogger = CreateLogger(
            infrastructureHandler,
            "System.Net.Http.HttpClient.BioTwin",
            LogLevel.Warning);

        infrastructureLogger.LogError("Transport failed");
        await AssertNoRequestAsync(infrastructureHandler);
    }

    [Fact]
    public async Task Sends_startup_information()
    {
        var handler = new RecordingHandler();
        var logger = CreateLogger(handler, "BioTwin_AI.BlazorClient.Startup", LogLevel.Warning);

        logger.LogInformation("Client started");
        await handler.Received.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var entry = await handler.ReadEntryAsync();
        Assert.Equal("Information", entry.Level);
        Assert.Equal("BioTwin_AI.BlazorClient.Startup", entry.Category);
    }

    [Fact]
    public async Task Truncates_oversized_category_message_and_exception()
    {
        var handler = new RecordingHandler();
        var logger = CreateLogger(handler, new string('c', 300), LogLevel.Warning);

        logger.LogError(
            new InvalidOperationException(new string('e', 10_000)),
            new string('m', 3_000));
        await handler.Received.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var entry = await handler.ReadEntryAsync();
        Assert.Equal(256, entry.Category.Length);
        Assert.Equal(2048, entry.Message.Length);
        Assert.NotNull(entry.Exception);
        Assert.Equal(8192, entry.Exception!.Length);
    }

    private static RemoteClientLogger CreateLogger(
        HttpMessageHandler handler,
        string categoryName,
        LogLevel minimumLevel)
    {
        var client = new HttpClient(handler) { BaseAddress = new Uri("https://api.example/") };
        return new RemoteClientLogger(
            client,
            "api/client-logs",
            categoryName,
            () => "https://client.example/resume/create?uid=secret#review",
            minimumLevel);
    }

    private static async Task AssertNoRequestAsync(RecordingHandler handler)
    {
        var completed = await Task.WhenAny(handler.Received.Task, Task.Delay(TimeSpan.FromMilliseconds(150)));
        Assert.NotSame(handler.Received.Task, completed);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        private readonly TaskCompletionSource<bool> _received = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource<bool> Received => _received;

        public string Body { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            _received.TrySetResult(true);
            return new HttpResponseMessage(HttpStatusCode.Accepted);
        }

        public Task<ClientLogEntryRequest> ReadEntryAsync()
        {
            return Task.FromResult(
                JsonSerializer.Deserialize<ClientLogEntryRequest>(Body, new JsonSerializerOptions(JsonSerializerDefaults.Web))!);
        }
    }
}
