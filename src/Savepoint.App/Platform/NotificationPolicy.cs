using Savepoint.Core.Api;

namespace Savepoint.App.Platform;

/// <summary>
/// Decides whether a finished job deserves an operating-system notification: only the long jobs a user walks away from
/// (a switch without playing, capture, compaction, storage move, clearing data), and only while the window is not in front
/// of them. Launches never notify, since the game opening is the signal.
/// </summary>
public static class NotificationPolicy
{
    /// <summary>True when jobs of this kind may notify at all, whatever the window is doing.</summary>
    public static bool IsNotifiable(JobKind kind) =>
        kind is JobKind.Switch or JobKind.Capture or JobKind.Compact or JobKind.RelocateStorage or JobKind.ClearData;

    /// <summary>True when a job of this kind that just finished should raise a notification.</summary>
    /// <param name="kind">The kind of the finished job.</param>
    /// <param name="window">The window state at the moment the job finished.</param>
    public static bool ShouldNotify(JobKind kind, WindowActivity window) =>
        IsNotifiable(kind) && (!window.IsActive || window.IsMinimized);

    /// <summary>The same decision against the live window state.</summary>
    public static bool ShouldNotify(JobKind kind, IWindowActivitySource window) => ShouldNotify(kind, window.Current);
}
