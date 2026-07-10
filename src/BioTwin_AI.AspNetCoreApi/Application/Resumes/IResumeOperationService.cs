using BioTwin_AI.DotNetShared.Resumes;

namespace BioTwin_AI.AspNetCoreApi.Application.Resumes;

public interface IResumeOperationService
{
    Task<ResumeOperationLeaseDto> AcquireAsync(int userId, string tenantId, string operationType, string expectedStateToken, CancellationToken cancellationToken);

    Task<ResumeOperationLeaseDto> HeartbeatAsync(int userId, string operationId, CancellationToken cancellationToken);

    Task ReleaseAsync(int userId, string operationId, CancellationToken cancellationToken);

    Task ValidateAsync(int userId, string tenantId, string operationId, string expectedStateToken, CancellationToken cancellationToken);
}
