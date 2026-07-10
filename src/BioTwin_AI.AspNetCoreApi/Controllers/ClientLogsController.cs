using BioTwin_AI.DotNetShared.Logging;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BioTwin_AI.AspNetCoreApi.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/client-logs")]
public sealed class ClientLogsController(ILogger<ClientLogsController> logger) : ControllerBase
{
    private const int MaxCategoryLength = 256;
    private const int MaxMessageLength = 2048;
    private const int MaxExceptionLength = 8192;
    private const int MaxUrlLength = 2048;
    private const string StartupCategory = "BioTwin_AI.BlazorClient.Startup";

    [HttpPost]
    public IActionResult Post(ClientLogEntryRequest request)
    {
        var logLevel = ParseLogLevel(request.Level);
        var category = Normalize(
            request.Category,
            "BioTwin_AI.BlazorClient",
            MaxCategoryLength);
        if (!ShouldLog(logLevel, category))
        {
            return Accepted();
        }

        var message = Normalize(request.Message, "(empty client log message)", MaxMessageLength);
        var exception = NormalizeOptional(request.Exception, MaxExceptionLength);
        var url = NormalizeOptional(SanitizeUrl(request.Url), MaxUrlLength);

        logger.Log(
            logLevel,
            "Client log [{ClientCategory}] {ClientMessage} Url={ClientUrl} ClientTimestamp={ClientTimestamp} Exception={ClientException}",
            category,
            message,
            url,
            request.Timestamp,
            exception);

        return Accepted();
    }

    private static LogLevel ParseLogLevel(string? level)
    {
        return Enum.TryParse<LogLevel>(level, ignoreCase: true, out var parsed)
            ? parsed
            : LogLevel.None;
    }

    private static bool ShouldLog(LogLevel logLevel, string category)
    {
        return logLevel is >= LogLevel.Warning and < LogLevel.None
            || (logLevel == LogLevel.Information
                && string.Equals(category, StartupCategory, StringComparison.Ordinal));
    }

    private static string Normalize(string? value, string fallback, int maximumLength)
    {
        var normalized = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        return normalized.Length <= maximumLength ? normalized : normalized[..maximumLength];
    }

    private static string? NormalizeOptional(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        return normalized.Length <= maximumLength ? normalized : normalized[..maximumLength];
    }

    private static string? SanitizeUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
        {
            return null;
        }

        if (Uri.TryCreate(url, UriKind.Absolute, out var absoluteUri))
        {
            return absoluteUri.AbsolutePath;
        }

        var path = url.Trim();
        var suffixIndex = path.IndexOfAny(['?', '#']);
        return suffixIndex >= 0 ? path[..suffixIndex] : path;
    }
}
