namespace Savepoint.Core.Tests;

// Git dates points to the whole second and compares a file's time with the index's to the second, so work saved in the same second as an
// earlier write is not told apart from it. A person saves more than a second apart, so a test that depends on the order does too.
internal static class GitClock
{
    private static readonly TimeSpan _pastTheSecond = TimeSpan.FromMilliseconds(1100);

    // Returns once the clock git reads is in a later second than it was at the call.
    internal static Task NextSecondAsync(CancellationToken cancellationToken) => Task.Delay(_pastTheSecond, cancellationToken);
}
