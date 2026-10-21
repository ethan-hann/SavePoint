namespace Savepoint.Core.Platform;

/// <summary>A process SavePoint started and can wait on.</summary>
/// <remarks>Disposing releases SavePoint's handle only; it never stops the game.</remarks>
public interface IGameProcess : IDisposable
{
    /// <summary>The OS process id.</summary>
    int Pid { get; }

    /// <summary>True once the process has exited.</summary>
    bool HasExited { get; }

    /// <summary>Null until the process has exited.</summary>
    int? ExitCode { get; }

    /// <summary>Completes when the process exits; throws <see cref="OperationCanceledException"/> when canceled first.</summary>
    /// <param name="cancellationToken">Stops the wait, not the process.</param>
    Task WaitForExitAsync(CancellationToken cancellationToken);
}

/// <summary>Starts games: an executable or app bundle, or a storefront link through the OS opener.</summary>
/// <remarks>
/// A launcher never runs anything through a shell string and never changes a file's permissions. Failures are typed:
/// <see cref="Errors.PathMissingException"/> when the file or bundle is not there, <see cref="Errors.LaunchWindowsProgramException"/>
/// for a Windows program on an OS that cannot start one, and <see cref="Errors.LaunchSpawnException"/> for everything else the OS refuses.
/// </remarks>
public interface IGameLauncher
{
    /// <summary>Starts an executable or app bundle without a shell; arguments are passed as a list.</summary>
    /// <param name="path">The executable or app bundle, as an absolute path.</param>
    /// <param name="args">The arguments, one list item each; nothing is shell-quoted.</param>
    /// <param name="workingDirectory">The folder the process starts in.</param>
    /// <returns>
    /// The process to wait on. For a macOS app bundle this is the <c>open</c> command, which stays alive until the app quits.
    /// </returns>
    IGameProcess StartExecutable(string path, IReadOnlyList<string> args, string workingDirectory);

    /// <summary>Hands a storefront or web link to the OS opener.</summary>
    /// <param name="uri">The link to open; it needs a scheme of at least two characters, and <c>file:</c> links are refused.</param>
    /// <param name="cancellationToken">Stops waiting for the opener; a link already handed over stays handed over.</param>
    /// <remarks>
    /// Where the opener is a command (<c>xdg-open</c>, <c>open</c>) this waits briefly for it and reports a failing exit code;
    /// an opener still running after that wait counts as handed over.
    /// </remarks>
    Task OpenUriAsync(string uri, CancellationToken cancellationToken = default);
}
