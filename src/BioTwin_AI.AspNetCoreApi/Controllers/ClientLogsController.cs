using BioTwin_AI.DotNetShared.Logging;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BioTwin_AI.AspNetCoreApi.Controllers;

[ApiController]
[AllowAnonymous]
[Route("api/client-logs")]
public sealed class ClientLogsController(ILogger<ClientLogsController> logger) : ControllerBase
{
    [HttpPost]
    public IActionResult Post(ClientLogEntryRequest request)
    {
        var logLevel = ParseLogLevel(request.Level);
        if (logLevel < LogLevel.Information || logLevel == LogLevel.Debug || logLevel == LogLevel.Trace)
        {
            return Accepted();
        }

        var category = string.IsNullOrWhiteSpace(request.Category)
            ? "BioTwin_AI.BlazorClient"
            : request.Category.Trim();
        var message = string.IsNullOrWhiteSpace(request.Message)
            ? "(empty client log message)"
            : request.Message.Trim();

        logger.Log(
            logLevel,
            "Client log [{ClientCategory}] {ClientMessage} Url={ClientUrl} ClientTimestamp={ClientTimestamp} Exception={ClientException}",
            category,
            message,
            request.Url,
            request.Timestamp,
            request.Exception);

        return Accepted();
    }

    private static LogLevel ParseLogLevel(string? level)
    {
        return Enum.TryParse<LogLevel>(level, ignoreCase: true, out var parsed)
            ? parsed
            : LogLevel.Information;
    }
}
