using BioTwin_AI.AspNetCoreApi.Controllers;
using BioTwin_AI.DotNetShared.Logging;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace BioTwin_AI.AspNetCoreApi.Tests.Controllers;

public sealed class ClientLogsControllerTests
{
    [Theory]
    [InlineData("Trace")]
    [InlineData("Debug")]
    [InlineData("Information")]
    [InlineData("invalid")]
    public void Low_priority_events_are_accepted_without_being_logged(string level)
    {
        var logger = new RecordingLogger<ClientLogsController>();
        var controller = new ClientLogsController(logger);

        var result = controller.Post(CreateRequest(level, "BioTwin_AI.BlazorClient.Pages.Home"));

        Assert.IsType<AcceptedResult>(result);
        Assert.Empty(logger.Entries);
    }

    [Fact]
    public void Startup_information_is_logged()
    {
        var logger = new RecordingLogger<ClientLogsController>();
        var controller = new ClientLogsController(logger);

        var result = controller.Post(CreateRequest("Information", "BioTwin_AI.BlazorClient.Startup"));

        Assert.IsType<AcceptedResult>(result);
        Assert.Single(logger.Entries);
    }

    [Fact]
    public void Warning_is_sanitized_bounded_and_logged_with_structured_values()
    {
        var logger = new RecordingLogger<ClientLogsController>();
        var controller = new ClientLogsController(logger);
        var request = new ClientLogEntryRequest(
            "Warning",
            new string('c', 300),
            new string('m', 2200),
            new string('e', 8400),
            "https://example.test/resume/create?uid=secret#review",
            DateTimeOffset.UtcNow);

        var result = controller.Post(request);

        Assert.IsType<AcceptedResult>(result);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Equal(256, Assert.IsType<string>(entry.Values["ClientCategory"]).Length);
        Assert.Equal(2048, Assert.IsType<string>(entry.Values["ClientMessage"]).Length);
        Assert.Equal(8192, Assert.IsType<string>(entry.Values["ClientException"]).Length);
        Assert.Equal("/resume/create", entry.Values["ClientUrl"]);
        Assert.DoesNotContain("secret", entry.Message, StringComparison.Ordinal);
    }

    private static ClientLogEntryRequest CreateRequest(string level, string category) =>
        new(level, category, "message", null, "/", DateTimeOffset.UtcNow);

    private sealed class RecordingLogger<T> : ILogger<T>
    {
        public List<LogEntry> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            var values = state is IEnumerable<KeyValuePair<string, object?>> properties
                ? properties.ToDictionary(item => item.Key, item => item.Value)
                : [];
            Entries.Add(new LogEntry(logLevel, formatter(state, exception), values));
        }
    }

    private sealed record LogEntry(
        LogLevel Level,
        string Message,
        IReadOnlyDictionary<string, object?> Values);
}
