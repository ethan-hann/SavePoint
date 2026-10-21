using Savepoint.Core.Errors;

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
    /// <param name="NewFileCount">Files in the saves folder that are new or changed since the session began.</param>
    /// <param name="ChangedInstallFileCount">
    /// Files in the install folder that changed while playing, which catches an update made mid-session.
    /// </param>
    /// <param name="Options">What the user may do with the session.</param>
    public sealed record Exited(
        DateTimeOffset StartedAt,
        TimeSpan PlayDuration,
        int NewFileCount,
        int ChangedInstallFileCount,
        PostPlayOptions Options) : SessionState;

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
