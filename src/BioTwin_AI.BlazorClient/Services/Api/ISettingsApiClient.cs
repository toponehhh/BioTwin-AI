using BioTwin_AI.DotNetShared.Common;

namespace BioTwin_AI.BlazorClient.Services.Api;

public interface ISettingsApiClient
{
    string ApiBaseUrl { get; }

    Task<HealthResponse> GetHealthAsync(CancellationToken cancellationToken = default);
}
