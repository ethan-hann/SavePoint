namespace Savepoint.Core.Git;

/// <summary>Tuning for <see cref="GitEngine"/>. The defaults are the ones the app uses.</summary>
public sealed record GitEngineOptions
{
    /// <summary>Free bytes that must remain on the storage volume before a capture or a repack starts (100 MiB).</summary>
    public long MinFreeBytesForStorageWrites { get; init; } = 100L * 1024 * 1024;

    /// <summary>Free bytes that must remain on the work tree's volume after a checkout has written its changes (100 MiB).</summary>
    public long MinFreeBytesForCheckout { get; init; } = 100L * 1024 * 1024;

    /// <summary>The clock used for the consent marker's confirmation time.</summary>
    public TimeProvider Clock { get; init; } = TimeProvider.System;

    // Sees every git call just before it runs; tests use it to assert argument lists and to inject faults.
    internal Action<GitInvocation>? InvocationObserver { get; init; }

    // Waits between the passes that re-sync paths a checkout left stale.
    internal IReadOnlyList<TimeSpan> ResyncDelays { get; init; } = [TimeSpan.FromMilliseconds(150), TimeSpan.FromMilliseconds(300)];

    // Waits between attempts to delete a git directory a just-stopped process may still hold.
    internal IReadOnlyList<TimeSpan> DeleteRetryDelays { get; init; } =
        [TimeSpan.FromMilliseconds(300), TimeSpan.FromMilliseconds(300), TimeSpan.FromMilliseconds(300), TimeSpan.FromMilliseconds(300)];

    // Waits between retries of a git write that found another process holding a lock.
    internal IReadOnlyList<TimeSpan> LockRetryDelays { get; init; } =
        [TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(250), TimeSpan.FromMilliseconds(500)];
}
