namespace Savepoint.Core.Tests.Git.Engine;

// A clock a test can set, so a timestamp written by the engine can be asserted exactly.
internal sealed class FixedClock : TimeProvider
{
    private DateTimeOffset _now;

    public FixedClock(DateTimeOffset now)
    {
        _now = now;
    }

    public void Set(DateTimeOffset now) => _now = now;

    public override DateTimeOffset GetUtcNow() => _now.ToUniversalTime();
}
