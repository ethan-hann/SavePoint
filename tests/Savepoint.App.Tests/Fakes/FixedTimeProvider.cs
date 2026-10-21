namespace Savepoint.App.Tests.Fakes;

// The clock only moves when a test moves it, so relative dates ("3 days ago") stay stable.
public sealed class FixedTimeProvider : TimeProvider
{
    public FixedTimeProvider(DateTimeOffset now)
    {
        Now = now;
    }

    public DateTimeOffset Now { get; set; }

    public override DateTimeOffset GetUtcNow() => Now.ToUniversalTime();

    public void Advance(TimeSpan by) => Now += by;
}
