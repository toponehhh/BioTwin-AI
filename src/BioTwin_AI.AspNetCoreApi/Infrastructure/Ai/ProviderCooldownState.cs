namespace BioTwin_AI.AspNetCoreApi.Infrastructure.Ai;

public sealed class ProviderCooldownState(TimeProvider timeProvider, TimeSpan duration)
{
    private long _retryAfterUtcTicks;

    public bool IsCoolingDown =>
        timeProvider.GetUtcNow().UtcTicks < Interlocked.Read(ref _retryAfterUtcTicks);

    public void MarkRetryableFailure()
    {
        var retryAfter = timeProvider.GetUtcNow().Add(duration).UtcTicks;
        Interlocked.Exchange(ref _retryAfterUtcTicks, retryAfter);
    }

    public void MarkHealthy()
    {
        Interlocked.Exchange(ref _retryAfterUtcTicks, 0);
    }
}
