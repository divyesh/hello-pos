namespace HelloPos.Api.Tests.Infrastructure;

/// <summary>A clock tests can move forward (token lifespans, session expiry).</summary>
public sealed class TestClock : TimeProvider
{
    private long offsetTicks;

    public override DateTimeOffset GetUtcNow() => base.GetUtcNow().AddTicks(Interlocked.Read(ref offsetTicks));

    public void Advance(TimeSpan by) => Interlocked.Add(ref offsetTicks, by.Ticks);
}
