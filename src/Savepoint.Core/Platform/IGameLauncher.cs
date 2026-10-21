namespace Savepoint.Core.Platform;

/// <summary>A process SavePoint started and can wait on.</summary>
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
public interface IGameLauncher
{
    /// <summary>Starts an executable or app bundle without a shell; arguments are passed as a list.</summary>
    /// <param name="path">The executable or app bundle.</param>
    /// <param name="args">The arguments, one list item each; nothing is shell-quoted.</param>
    /// <param name="workingDirectory">The folder the process starts in.</param>
    IGameProcess StartExecutable(string path, IReadOnlyList<string> args, string workingDirectory);

    /// <summary>Hands a storefront or web URI to the OS opener.</summary>
    /// <param name="uri">The link to open.</param>
    void OpenUri(string uri);
}
