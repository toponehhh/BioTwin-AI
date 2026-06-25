using BioTwin_AI.DotNetShared.Common;

namespace BioTwin_AI.BlazorClient.Services.Api;

public sealed class SettingsApiClient(HttpClient httpClient) : ApiClientBase(httpClient), ISettingsApiClient
{
    public string ApiBaseUrl => HttpClient.BaseAddress?.ToString() ?? string.Empty;

    public Task<HealthResponse> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        return GetAsync<HealthResponse>("api/health", cancellationToken);
    }
}
