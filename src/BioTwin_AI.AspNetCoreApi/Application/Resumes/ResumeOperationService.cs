using BioTwin_AI.DotNetShared.Resumes;

namespace BioTwin_AI.AspNetCoreApi.Application.Resumes;

public sealed class ResumeOperationService(
    IResumeOperationCoordinator coordinator,
    IResumeStateTokenService stateTokenService,
    TimeProvider timeProvider) : IResumeOperationService
{
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromMinutes(2);

    public async Task<ResumeOperationLeaseDto> AcquireAsync(
        int userId,
        string tenantId,
        string operationType,
        string expectedStateToken,
        CancellationToken cancellationToken)
    {
        var currentToken = await stateTokenService.ComputeAsync(tenantId, cancellationToken);
        EnsureStateIsCurrent(currentToken, expectedStateToken);

        var lease = await coordinator.TryAcquireAsync(
            userId,
            NormalizeOperationType(operationType),
            currentToken,
            timeProvider.GetUtcNow(),
            LeaseDuration,
            cancellationToken);

        return lease ?? throw new ResumeOperationConflictException(
            "resume_operation_in_progress",
            "A resume is already being created or imported.",
            "Return to the other client, or wait for its session to expire.");
    }

    public async Task<ResumeOperationLeaseDto> HeartbeatAsync(
        int userId,
        string operationId,
        CancellationToken cancellationToken)
    {
        return await coordinator.RenewAsync(
                userId,
                operationId,
                timeProvider.GetUtcNow(),
                LeaseDuration,
                cancellationToken)
            ?? throw OperationNotFound();
    }

    public async Task ReleaseAsync(int userId, string operationId, CancellationToken cancellationToken)
    {
        if (!await coordinator.ReleaseAsync(userId, operationId, cancellationToken))
        {
            throw OperationNotFound();
        }
    }

    public async Task ValidateAsync(
        int userId,
        string tenantId,
        string operationId,
        string expectedStateToken,
        CancellationToken cancellationToken)
    {
        var lease = await coordinator.GetAsync(userId, cancellationToken);
        if (lease is null
            || lease.ExpiresAt <= timeProvider.GetUtcNow()
            || !string.Equals(lease.OperationId, operationId, StringComparison.Ordinal))
        {
            throw OperationNotFound();
        }

        var currentToken = await stateTokenService.ComputeAsync(tenantId, cancellationToken);
        EnsureStateIsCurrent(currentToken, expectedStateToken);
        EnsureStateIsCurrent(lease.ResumeStateToken, expectedStateToken);
    }

    private static void EnsureStateIsCurrent(string currentToken, string expectedStateToken)
    {
        if (!string.Equals(currentToken, expectedStateToken, StringComparison.Ordinal))
        {
            throw new ResumeOperationConflictException(
                "resume_state_stale",
                "The resume changed after this page was loaded.",
                "Refresh the resume list and review the latest version before trying again.");
        }
    }

    private static string NormalizeOperationType(string operationType)
    {
        var normalized = operationType.Trim().ToLowerInvariant();
        return normalized is "create" or "import"
            ? normalized
            : throw new ArgumentException("Operation type must be 'create' or 'import'.", nameof(operationType));
    }

    private static ResumeOperationConflictException OperationNotFound()
    {
        return new ResumeOperationConflictException(
            "resume_operation_not_found",
            "The resume operation is no longer active.",
            "Refresh the page and start the operation again.",
            StatusCodes.Status404NotFound);
    }
}
