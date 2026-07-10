using System.Net;
using System.Net.Http.Json;
using BioTwin_AI.DotNetShared.Profiles;

namespace BioTwin_AI.BlazorClient.Services.Api;

public sealed class PublicProfileApiClient(HttpClient httpClient, ILogger<PublicProfileApiClient> logger)
    : ApiClientBase(httpClient, logger), IPublicProfileApiClient
{
    public async Task<CandidateProfileDto?> GetCandidateProfileAsync(string? uid, CancellationToken cancellationToken = default)
    {
        var path = string.IsNullOrWhiteSpace(uid)
            ? "api/public/candidate-profile"
            : $"api/public/candidate-profile?uid={Uri.EscapeDataString(uid)}";

        using var request = CreateCredentialedRequest(HttpMethod.Get, path);
        using var response = await SendLoggedAsync(request, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        await EnsureSuccessAsync(response, cancellationToken);
        return await response.Content.ReadFromJsonAsync<CandidateProfileDto>(cancellationToken);
    }
}
