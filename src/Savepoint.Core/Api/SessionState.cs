using Savepoint.Core.Errors;

namespace Savepoint.Core.Api;

public abstract record SessionState
{
    // Closed: the launch flow switches over exactly these phases.
    private SessionState()
    {
    }

    // Checking folders before anything moves.
    public sealed record Preparing() : SessionState;

    public sealed record Switching(JobProgress Progress) : SessionState;

    public sealed record Running(DateTimeOffset StartedAt) : SessionState;

    // NewFileCount counts the saves folder; ChangedInstallFileCount catches an update made while playing.
    public sealed record Exited(
        DateTimeOffset StartedAt,
        TimeSpan PlayDuration,
        int NewFileCount,
        int ChangedInstallFileCount,
        PostPlayOptions Options) : SessionState;

    public sealed record Completing(SessionCompletion Completion, Guid JobId) : SessionState;

    // Result is null for Skip, and Error is set when the capture failed as a whole.
    public sealed record Completed(SessionCompletion Completion, CaptureResult? Result, ErrorInfo? Error) : SessionState;

    // The game never started: the switch failed, was cancelled, or the launch itself failed.
    public sealed record Failed(ErrorInfo Error) : SessionState;
}
