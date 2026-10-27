namespace Savepoint.App.Platform.Processes;

/// <summary>A program and its arguments, one list item each; nothing is joined into a shell string.</summary>
/// <param name="FileName">The program to start: an absolute path, or a name found on PATH.</param>
/// <param name="Arguments">The arguments in order, exactly as the program should receive them.</param>
internal sealed record ProcessCommand(string FileName, IReadOnlyList<string> Arguments);

/// <summary>What a finished process left behind.</summary>
/// <param name="ExitCode">The exit code.</param>
/// <param name="StandardOutput">Everything the process wrote to standard output.</param>
internal sealed record ProcessOutput(int ExitCode, string StandardOutput);

/// <summary>Starts the helper programs the OS integration needs, behind a seam so tests never start a real one.</summary>
internal interface IProcessRunner
{
    /// <summary>Starts the program and returns without waiting for it.</summary>
    /// <returns>False when the program cannot be started.</returns>
    bool TryStart(ProcessCommand command);

    /// <summary>
    /// Starts the program and waits up to <paramref name="grace"/> to see whether it fails at once. A program still running when the
    /// grace ends is left running (it may be the file manager itself) and counts as started.
    /// </summary>
    /// <param name="command">What to start.</param>
    /// <param name="grace">How long to watch for an early exit.</param>
    /// <param name="cancellationToken">Stops the watching; the program is left running.</param>
    /// <returns>False when the program cannot be started or ends within the grace with a non-zero exit code.</returns>
    Task<bool> TryStartAndConfirmAsync(ProcessCommand command, TimeSpan grace, CancellationToken cancellationToken = default);

    /// <summary>Runs the program to its end and captures what it printed.</summary>
    /// <param name="command">What to run.</param>
    /// <param name="timeout">How long to wait before the process tree is killed.</param>
    /// <param name="cancellationToken">Kills the process tree when canceled.</param>
    /// <returns>Null when the program cannot be started, runs past the timeout, or is canceled.</returns>
    Task<ProcessOutput?> RunAsync(ProcessCommand command, TimeSpan timeout, CancellationToken cancellationToken = default);
}
