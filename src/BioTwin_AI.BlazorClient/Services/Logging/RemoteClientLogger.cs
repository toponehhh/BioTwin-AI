using System.Net.Http.Json;
using BioTwin_AI.DotNetShared.Logging;
using Microsoft.Extensions.Logging;

namespace BioTwin_AI.BlazorClient.Services.Logging;

public sealed class RemoteClientLogger : ILogger
{
    private readonly HttpClient _httpClient;
    private readonly string _endpoint;
    private readonly string _categoryName;
    private readonly LogLevel _minimumLevel;

    public RemoteClientLogger(
        HttpClient httpClient,
        string endpoint,
        string categoryName,
        LogLevel minimumLevel)
    {
        _httpClient = httpClient;
        _endpoint = endpoint;
        _categoryName = categoryName;
        _minimumLevel = minimumLevel;
    }

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull
    {
        return null;
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return logLevel >= _minimumLevel
            && logLevel != LogLevel.None
            && !IsRemoteLoggingInfrastructureCategory(_categoryName);
    }

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (logLevel < _minimumLevel || !IsEnabled(logLevel))
        {
            return;
        }

        var message = formatter(state, exception);
        if (string.IsNullOrWhiteSpace(message) && exception is null)
        {
            return;
        }

        var request = new ClientLogEntryRequest(
            logLevel.ToString(),
            _categoryName,
            message,
            exception?.ToString(),
            _httpClient.BaseAddress?.ToString(),
            DateTimeOffset.UtcNow);

        _ = SendAsync(request);
    }

    private async Task SendAsync(ClientLogEntryRequest request)
    {
        try
        {
            using var response = await _httpClient.PostAsJsonAsync(_endpoint, request);
        }
        catch
        {
            // Client log forwarding must never break the browser application.
        }
    }

    private static bool IsRemoteLoggingInfrastructureCategory(string category)
    {
        return category.StartsWith("System.Net.Http", StringComparison.Ordinal)
            || category.Contains(nameof(RemoteClientLogger), StringComparison.Ordinal);
    }
}
