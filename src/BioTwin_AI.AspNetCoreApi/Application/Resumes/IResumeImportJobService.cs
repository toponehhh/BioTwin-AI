using BioTwin_AI.DotNetShared.Resumes;

namespace BioTwin_AI.AspNetCoreApi.Application.Resumes;

public interface IResumeImportJobService
{
    Task<ResumeImportJobDto> StartWizardAsync(
        int userId,
        string tenantId,
        string operationId,
        string expectedStateToken,
        IFormFile file,
        string language,
        string? title,
        CancellationToken cancellationToken);

    Task<ResumeImportJobDto> StartWorkspaceAsync(
        int userId,
        string tenantId,
        string operationId,
        string expectedStateToken,
        IReadOnlyList<IFormFile> files,
        CancellationToken cancellationToken);

    Task<ResumeImportJobDto?> GetAsync(string tenantId, string jobId, CancellationToken cancellationToken);

    Task<bool> CancelAsync(string tenantId, string jobId, CancellationToken cancellationToken);
}
