using BioTwin_AI.DotNetShared.Profiles;

namespace BioTwin_AI.BlazorClient.Services.Api;

public interface IPublicProfileApiClient
{
    Task<CandidateProfileDto?> GetCandidateProfileAsync(string? uid, CancellationToken cancellationToken = default);
}
