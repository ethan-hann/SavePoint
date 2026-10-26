using Savepoint.Core.Errors;
using Savepoint.Core.Launch;

namespace Savepoint.Core.Api;

/// <summary>
/// Where a play session is. The cases are closed and the launch flow switches over exactly these phases, in this order: Preparing,
/// Switching, Running, Exited, Completing, Completed. Failed can replace any phase before Running.
/// </summary>
public abstract record SessionState
{
    private SessionState()
    {
    }

    /// <summary>Checking both folders before anything moves.</summary>
    public sealed record Preparing() : SessionState;

    /// <summary>Moving the folders to the target. Cancel it through <see cref="IJobsApi.CancelAsync"/> on the launch job.</summary>
    /// <param name="Progress">How far the switch has got.</param>
    public sealed record Switching(JobProgress Progress) : SessionState;

    /// <summary>The game is running.</summary>
    /// <param name="StartedAt">When the game started, as an absolute instant.</param>
    public sealed record Running(DateTimeOffset StartedAt) : SessionState;

    /// <summary>The game has stopped, or the user said they are done playing. The post-play choice is pending.</summary>
    /// <param name="StartedAt">When the game started, as an absolute instant.</param>
    /// <param name="PlayDuration">How long the session lasted.</param>
    /// <param name="NewFileCount">
    /// Files in the saves folder that are new or changed since the session began, a file another program holds open or that cannot be read
    /// included, since the save cannot tell it from a changed one. 0 when the save will leave the folder alone because it is on another
    /// profile's history.
    /// </param>
    /// <param name="ChangedInstallFileCount">
    /// Files in the install folder that changed while playing, which catches an update made mid-session; counted as <paramref name="NewFileCount"/> is.
    /// </param>
    /// <param name="Options">What the user may do with the session.</param>
    /// <param name="EndKind">
    /// How the end was found. <see cref="SessionEndKind.NamedProcessNeverAppeared"/> means the game's process name never matched a running
    /// program: <paramref name="PlayDuration"/> is then only the wait for it, not time played, and callers should not present it as play time.
    /// </param>
    /// <param name="LeaveOutRules">
    /// Set when every file counted in <paramref name="ChangedInstallFileCount"/> is run-time output (logs and crash dumps, see
    /// <see cref="Savepoint.Core.Catalog.RunTimeOutput"/>): the leave-out rules that cover those files, which the player may add to the game.
    /// Null when no install file changed, or when any changed file, or any file that could not be read, is not run-time output.
    /// </param>
    /// <param name="ProgramExitCode">
    /// The exit code of the program SavePoint started, set only when <paramref name="EndKind"/> is <see cref="SessionEndKind.ProgramFailedAtStart"/>; null otherwise.
    /// </param>
    public sealed record Exited(
        DateTimeOffset StartedAt,
        TimeSpan PlayDuration,
        int NewFileCount,
        int ChangedInstallFileCount,
        PostPlayOptions Options,
        SessionEndKind EndKind = SessionEndKind.ChildExited,
        IReadOnlyList<string>? LeaveOutRules = null,
        int? ProgramExitCode = null) : SessionState;

    /// <summary>The chosen completion is running as a capture job.</summary>
    /// <param name="Completion">What the user chose.</param>
    /// <param name="JobId">The capture job; cancel it through <see cref="IJobsApi.CancelAsync"/>.</param>
    public sealed record Completing(SessionCompletion Completion, Guid JobId) : SessionState;

    /// <summary>The session is over. It stays visible until the next launch of the game.</summary>
    /// <param name="Completion">What the user chose.</param>
    /// <param name="Result">The capture's result; null for <see cref="SessionCompletion.Skip"/>, which captures nothing.</param>
    /// <param name="Error">Set when the capture failed or was cancelled as a whole; null otherwise, including when only some folders failed.</param>
    public sealed record Completed(SessionCompletion Completion, CaptureResult? Result, ErrorInfo? Error) : SessionState;

    /// <summary>
    /// The game never started: the switch failed or was cancelled, or the launch itself failed. It stays visible until the next
    /// launch.
    /// </summary>
    /// <param name="Error">Why the launch failed.</param>
    public sealed record Failed(ErrorInfo Error) : SessionState;
}
