using System.Net.Http.Json;
using BioTwin_AI.DotNetShared.Logging;
using Microsoft.Extensions.Logging;

namespace BioTwin_AI.BlazorClient.Services.Logging;

public sealed class RemoteClientLogger : ILogger
{
    private const int MaxCategoryLength = 256;
    private const int MaxMessageLength = 2048;
    private const int MaxExceptionLength = 8192;
    private const int MaxUrlLength = 2048;
    private const string StartupCategory = "BioTwin_AI.BlazorClient.Startup";

    private readonly HttpClient _httpClient;
    private readonly string _endpoint;
    private readonly string _categoryName;
    private readonly Func<string?> _currentPageProvider;
    private readonly LogLevel _minimumLevel;

    public RemoteClientLogger(
        HttpClient httpClient,
        string endpoint,
        string categoryName,
        Func<string?> currentPageProvider,
        LogLevel minimumLevel)
    {
        _httpClient = httpClient;
        _endpoint = endpoint;
        _categoryName = categoryName;
        _currentPageProvider = currentPageProvider;
        _minimumLevel = minimumLevel;
    }

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull
    {
        return null;
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return logLevel != LogLevel.None
            && !IsRemoteLoggingInfrastructureCategory(_categoryName)
            && (logLevel >= _minimumLevel
                || (logLevel == LogLevel.Information
                    && string.Equals(_categoryName, StartupCategory, StringComparison.Ordinal)));
    }

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
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
            Truncate(_categoryName, MaxCategoryLength),
            Truncate(message, MaxMessageLength),
            exception is null ? null : Truncate(exception.ToString(), MaxExceptionLength),
            TruncateOptional(SanitizeUrl(_currentPageProvider()), MaxUrlLength),
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

    private static string? SanitizeUrl(string? url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var uri)
            ? uri.AbsolutePath
            : null;
    }

    private static string Truncate(string value, int maximumLength)
    {
        return value.Length <= maximumLength ? value : value[..maximumLength];
    }

    private static string? TruncateOptional(string? value, int maximumLength)
    {
        return value is null ? null : Truncate(value, maximumLength);
    }
}
