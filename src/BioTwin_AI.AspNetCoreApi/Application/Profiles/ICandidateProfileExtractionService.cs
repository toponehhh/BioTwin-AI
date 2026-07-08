namespace BioTwin_AI.AspNetCoreApi.Application.Profiles;

public interface ICandidateProfileExtractionService
{
    Task GenerateFromResumeAsync(int userId, string markdown, CancellationToken cancellationToken);
}
