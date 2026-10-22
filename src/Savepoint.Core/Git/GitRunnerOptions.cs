using Savepoint.Core.Diagnostics;

namespace Savepoint.Core.Git;

/// <summary>Tuning for <see cref="GitRunner"/>; the defaults suit the app, and tests shorten the waits.</summary>
public sealed record GitRunnerOptions
{
    /// <summary>Where failed git commands are written (D34); nothing is written when null.</summary>
    public IDiagnosticLog? Log { get; init; }

    /// <summary>The size of the buffer each output pipe is read with, in bytes; it must be positive.</summary>
    public int ReadBufferSize { get; init; } = 16 * 1024;

    /// <summary>How long a cancelled call waits for git (and the processes it started) to exit after they were killed, before it gives up and reports the cancellation anyway.</summary>
    public TimeSpan KillWaitTimeout { get; init; } = TimeSpan.FromSeconds(5);

    /// <summary>How long a call keeps waiting for git's output after git itself has exited; a leftover grandchild holding a pipe open cannot keep a call waiting longer than this.</summary>
    public TimeSpan DrainTimeout { get; init; } = TimeSpan.FromSeconds(5);
}
