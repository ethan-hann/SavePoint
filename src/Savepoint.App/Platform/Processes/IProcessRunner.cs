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

    /// <summary>Runs the program to its end and captures what it printed.</summary>
    /// <param name="command">What to run.</param>
    /// <param name="timeout">How long to wait before the process tree is killed.</param>
    /// <param name="cancellationToken">Kills the process tree when canceled.</param>
    /// <returns>Null when the program cannot be started, runs past the timeout, or is canceled.</returns>
    Task<ProcessOutput?> RunAsync(ProcessCommand command, TimeSpan timeout, CancellationToken cancellationToken = default);
}
