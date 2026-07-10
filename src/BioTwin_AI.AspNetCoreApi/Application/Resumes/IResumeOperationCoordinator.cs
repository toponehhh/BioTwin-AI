using BioTwin_AI.DotNetShared.Resumes;

namespace BioTwin_AI.AspNetCoreApi.Application.Resumes;

public interface IResumeOperationCoordinator
{
    ValueTask<ResumeOperationLeaseDto?> TryAcquireAsync(
        int userId,
        string operationType,
        string stateToken,
        DateTimeOffset now,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken);

    ValueTask<ResumeOperationLeaseDto?> RenewAsync(
        int userId,
        string operationId,
        DateTimeOffset now,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken);

    ValueTask<bool> ReleaseAsync(int userId, string operationId, CancellationToken cancellationToken);

    ValueTask<ResumeOperationLeaseDto?> GetAsync(int userId, CancellationToken cancellationToken);
}
