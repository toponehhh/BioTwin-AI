namespace BioTwin_AI.AspNetCoreApi.Application.Resumes;

public interface IResumeStateTokenService
{
    Task<string> ComputeAsync(string tenantId, CancellationToken cancellationToken);
}
