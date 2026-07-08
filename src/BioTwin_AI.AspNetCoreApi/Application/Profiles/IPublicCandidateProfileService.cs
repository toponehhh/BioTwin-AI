using BioTwin_AI.DotNetShared.Profiles;

namespace BioTwin_AI.AspNetCoreApi.Application.Profiles;

public interface IPublicCandidateProfileService
{
    Task<CandidateProfileDto?> GetAsync(string? uid, CancellationToken cancellationToken);
}
