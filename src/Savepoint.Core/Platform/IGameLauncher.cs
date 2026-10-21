namespace Savepoint.Core.Platform;

/// <summary>A process SavePoint started and can wait on.</summary>
public interface IGameProcess : IDisposable
{
    int Pid { get; }

    bool HasExited { get; }

    /// <summary>Null until the process has exited.</summary>
    int? ExitCode { get; }

    Task WaitForExitAsync(CancellationToken cancellationToken);
}

public interface IGameLauncher
{
    /// <summary>Starts an executable or app bundle without a shell; arguments are passed as a list.</summary>
    IGameProcess StartExecutable(string path, IReadOnlyList<string> args, string workingDirectory);

    /// <summary>Hands a storefront or web URI to the OS opener.</summary>
    void OpenUri(string uri);
}
