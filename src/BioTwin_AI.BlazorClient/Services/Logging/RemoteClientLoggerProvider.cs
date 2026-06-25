using Microsoft.Extensions.Logging;

namespace BioTwin_AI.BlazorClient.Services.Logging;

public sealed class RemoteClientLoggerProvider(
    HttpClient httpClient,
    string endpoint,
    LogLevel minimumLevel = LogLevel.Information) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName)
    {
        return new RemoteClientLogger(httpClient, endpoint, categoryName, minimumLevel);
    }

    public void Dispose()
    {
    }
}
