using Savepoint.Core.Diagnostics;

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

    // Overrides whether the operating system treats names that differ only in case as one file (Windows and macOS do, Linux does not);
    // a test sets it to run the comparison another system uses. A drive that ignores case is still found out and still counts.
    internal bool? SystemIgnoresCase { get; init; }

    // Sees every git call just before it runs; tests use it to assert argument lists and to inject faults.
    internal Action<GitInvocation>? InvocationObserver { get; init; }

    // Called with the path of each file the engine reads in full to compare it with a snapshot's copy; tests count the reads or make one fail.
    internal Action<string>? FileHashed { get; init; }

    // Whether a switch, discard or clean keeps the game folder from being renamed or deleted while it runs (Windows); tests that swap the
    // folder to see the later check refuse it turn this off.
    internal bool HoldFolderOpen { get; init; } = true;

    /// <summary>Where the engine records what it could not do for a folder, such as holding it open; null writes nothing.</summary>
    public IDiagnosticLog? Log { get; init; }

    // Called with the name of a step ("hold", "remove-displaced", "remove-new", "remove-leftover", "make-folders") just before the engine
    // looks at the guarded folder again for it; tests swap the folder here to see the look refuse it.
    internal Action<string>? BeforeFileChanges { get; init; }

    // Called with the same names right after that look passed and before the step changes any file; tests change a link here to see that
    // the step still works on the folder that was looked at.
    internal Action<string>? AfterFileRecheck { get; init; }

    // True while a git that an earlier, stopped SavePoint left running could not be ended: nothing it may be using is cleaned up, cleared or reclaimed.
    internal Func<bool>? LeftoverGitRunning { get; init; }

    // Called with a git directory just before its files are deleted, on the thread that deletes them; tests block in it to see who is kept waiting.
    internal Action<string>? BeforeGitDirDelete { get; init; }

    // How many bytes the object store at a path holds; tests replace it to fake the growth of a large file being stored.
    internal Func<string, long>? ObjectStoreSize { get; init; }

    // The wait between two looks at the object store while files are staged; tests replace it so no look depends on a clock.
    internal Func<CancellationToken, Task>? SampleWait { get; init; }

    // Writes the keep file at a path; tests replace it to make the write fail.
    internal Action<string>? WriteKeep { get; init; }

    // How a capture that reports progress splits its files into adds; tests shrink the limits to see several adds with a few files.
    internal StagingLimits StagingLimits { get; init; } = StagingLimits.Default;

    // Whether a listed file is still in the folder; tests replace it to make a file that cannot be looked at.
    internal Func<string, Presence>? PresenceProbe { get; init; }

    // Waits between the passes that re-sync paths a checkout left stale.
    internal IReadOnlyList<TimeSpan> ResyncDelays { get; init; } = [TimeSpan.FromMilliseconds(150), TimeSpan.FromMilliseconds(300)];

    // Waits between attempts to delete a git directory a just-stopped process may still hold.
    internal IReadOnlyList<TimeSpan> DeleteRetryDelays { get; init; } =
        [TimeSpan.FromMilliseconds(300), TimeSpan.FromMilliseconds(300), TimeSpan.FromMilliseconds(300), TimeSpan.FromMilliseconds(300)];

    // Waits between retries of a git write that found another process holding a lock.
    internal IReadOnlyList<TimeSpan> LockRetryDelays { get; init; } =
        [TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(250), TimeSpan.FromMilliseconds(500)];
}
