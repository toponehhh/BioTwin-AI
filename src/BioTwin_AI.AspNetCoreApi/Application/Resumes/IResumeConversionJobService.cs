using BioTwin_AI.DotNetShared.Resumes;

namespace BioTwin_AI.AspNetCoreApi.Application.Resumes;

public interface IResumeConversionJobService
{
    Task<ResumeConversionJobDto> StartAsync(
        string tenantId,
        IFormFile file,
        CancellationToken cancellationToken);

    Task<ResumeConversionJobDto?> GetAsync(
        string tenantId,
        string jobId,
        CancellationToken cancellationToken);
}
