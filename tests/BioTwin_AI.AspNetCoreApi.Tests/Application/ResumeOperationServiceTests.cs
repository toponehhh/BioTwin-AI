using BioTwin_AI.AspNetCoreApi.Application.Resumes;

namespace BioTwin_AI.AspNetCoreApi.Tests.Application;

public sealed class ResumeOperationServiceTests
{
    [Fact]
    public async Task AcquireAsync_rejects_a_second_active_operation_for_the_same_user()
    {
        var fixture = new Fixture();
        await fixture.Service.AcquireAsync(7, "huangd", "import", "state-a", CancellationToken.None);

        var exception = await Assert.ThrowsAsync<ResumeOperationConflictException>(() =>
            fixture.Service.AcquireAsync(7, "huangd", "create", "state-a", CancellationToken.None));

        Assert.Equal("resume_operation_in_progress", exception.Code);
        Assert.Contains("already", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AcquireAsync_allows_different_users_and_rejects_stale_state()
    {
        var fixture = new Fixture();

        var first = await fixture.Service.AcquireAsync(7, "huangd", "import", "state-a", CancellationToken.None);
        var second = await fixture.Service.AcquireAsync(8, "other", "import", "state-a", CancellationToken.None);

        Assert.NotEqual(first.OperationId, second.OperationId);
        var stale = await Assert.ThrowsAsync<ResumeOperationConflictException>(() =>
            fixture.Service.AcquireAsync(9, "huangd", "import", "old-state", CancellationToken.None));
        Assert.Equal("resume_state_stale", stale.Code);
    }

    [Fact]
    public async Task Heartbeat_renews_and_release_allows_a_new_operation()
    {
        var fixture = new Fixture();
        var lease = await fixture.Service.AcquireAsync(7, "huangd", "import", "state-a", CancellationToken.None);
        fixture.Clock.Advance(TimeSpan.FromSeconds(30));

        var renewed = await fixture.Service.HeartbeatAsync(7, lease.OperationId, CancellationToken.None);
        await fixture.Service.ReleaseAsync(7, lease.OperationId, CancellationToken.None);
        var replacement = await fixture.Service.AcquireAsync(7, "huangd", "create", "state-a", CancellationToken.None);

        Assert.True(renewed.ExpiresAt > lease.ExpiresAt);
        Assert.NotEqual(lease.OperationId, replacement.OperationId);
    }

    [Fact]
    public async Task Expired_operation_can_be_replaced_and_old_operation_cannot_validate()
    {
        var fixture = new Fixture();
        var lease = await fixture.Service.AcquireAsync(7, "huangd", "import", "state-a", CancellationToken.None);
        fixture.Clock.Advance(TimeSpan.FromMinutes(3));

        var replacement = await fixture.Service.AcquireAsync(7, "huangd", "create", "state-a", CancellationToken.None);
        var exception = await Assert.ThrowsAsync<ResumeOperationConflictException>(() =>
            fixture.Service.ValidateAsync(7, "huangd", lease.OperationId, "state-a", CancellationToken.None));

        Assert.NotEqual(lease.OperationId, replacement.OperationId);
        Assert.Equal("resume_operation_not_found", exception.Code);
    }

    private sealed class Fixture
    {
        public ManualTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 7, 9, 0, 0, 0, TimeSpan.Zero));

        public ResumeOperationService Service { get; }

        public Fixture()
        {
            Service = new ResumeOperationService(
                new MemoryResumeOperationCoordinator(),
                new StubStateTokenService(),
                Clock);
        }
    }

    private sealed class StubStateTokenService : IResumeStateTokenService
    {
        public Task<string> ComputeAsync(string tenantId, CancellationToken cancellationToken)
        {
            return Task.FromResult("state-a");
        }
    }

    private sealed class ManualTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset current = utcNow;

        public override DateTimeOffset GetUtcNow() => current;

        public void Advance(TimeSpan duration) => current += duration;
    }
}
