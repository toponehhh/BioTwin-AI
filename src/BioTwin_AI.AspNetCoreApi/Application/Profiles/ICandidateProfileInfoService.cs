using BioTwin_AI.AspNetCoreApi.Infrastructure.Data.Entities;

namespace BioTwin_AI.AspNetCoreApi.Application.Profiles;

public interface ICandidateProfileInfoService
{
    Task<CandidateProfileInfo> CreateNextVersionAsync(
        int userId,
        string infoType,
        string jsonData,
        string source,
        int? sourceResumeVersion,
        int? basedOnInfoId,
        int? createdByUserId,
        CancellationToken cancellationToken);

    Task<CandidateProfileInfo?> GetCurrentAsync(int userId, string infoType, CancellationToken cancellationToken);
}
