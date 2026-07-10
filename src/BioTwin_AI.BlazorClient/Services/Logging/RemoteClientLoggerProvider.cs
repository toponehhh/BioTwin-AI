using Microsoft.Extensions.Logging;

namespace BioTwin_AI.BlazorClient.Services.Logging;

public sealed class RemoteClientLoggerProvider(
    HttpClient httpClient,
    string endpoint,
    Func<string?> currentPageProvider,
    LogLevel minimumLevel = LogLevel.Information) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName)
    {
        return new RemoteClientLogger(httpClient, endpoint, categoryName, currentPageProvider, minimumLevel);
    }

    public void Dispose()
    {
    }
}
