using System.Collections.Concurrent;
using BioTwin_AI.DotNetShared.Resumes;

namespace BioTwin_AI.AspNetCoreApi.Application.Resumes;

public sealed class MemoryResumeOperationCoordinator : IResumeOperationCoordinator
{
    private readonly ConcurrentDictionary<int, LeaseState> leases = new();

    public ValueTask<ResumeOperationLeaseDto?> TryAcquireAsync(
        int userId,
        string operationType,
        string stateToken,
        DateTimeOffset now,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var next = new LeaseState(
            Guid.NewGuid().ToString("N"),
            operationType,
            stateToken,
            now.Add(leaseDuration));

        while (true)
        {
            if (!leases.TryGetValue(userId, out var current))
            {
                if (leases.TryAdd(userId, next))
                {
                    return ValueTask.FromResult<ResumeOperationLeaseDto?>(ToDto(next));
                }

                continue;
            }

            if (current.ExpiresAt > now)
            {
                return ValueTask.FromResult<ResumeOperationLeaseDto?>(null);
            }

            if (leases.TryUpdate(userId, next, current))
            {
                return ValueTask.FromResult<ResumeOperationLeaseDto?>(ToDto(next));
            }
        }
    }

    public ValueTask<ResumeOperationLeaseDto?> RenewAsync(
        int userId,
        string operationId,
        DateTimeOffset now,
        TimeSpan leaseDuration,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        while (leases.TryGetValue(userId, out var current))
        {
            if (!string.Equals(current.OperationId, operationId, StringComparison.Ordinal)
                || current.ExpiresAt <= now)
            {
                return ValueTask.FromResult<ResumeOperationLeaseDto?>(null);
            }

            var renewed = current with { ExpiresAt = now.Add(leaseDuration) };
            if (leases.TryUpdate(userId, renewed, current))
            {
                return ValueTask.FromResult<ResumeOperationLeaseDto?>(ToDto(renewed));
            }
        }

        return ValueTask.FromResult<ResumeOperationLeaseDto?>(null);
    }

    public ValueTask<bool> ReleaseAsync(int userId, string operationId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        while (leases.TryGetValue(userId, out var current))
        {
            if (!string.Equals(current.OperationId, operationId, StringComparison.Ordinal))
            {
                return ValueTask.FromResult(false);
            }

            if (leases.TryRemove(new KeyValuePair<int, LeaseState>(userId, current)))
            {
                return ValueTask.FromResult(true);
            }
        }

        return ValueTask.FromResult(false);
    }

    public ValueTask<ResumeOperationLeaseDto?> GetAsync(int userId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(
            leases.TryGetValue(userId, out var current)
                ? ToDto(current)
                : null);
    }

    private static ResumeOperationLeaseDto ToDto(LeaseState state)
    {
        return new ResumeOperationLeaseDto(state.OperationId, state.StateToken, state.ExpiresAt);
    }

    private sealed record LeaseState(
        string OperationId,
        string OperationType,
        string StateToken,
        DateTimeOffset ExpiresAt);
}
