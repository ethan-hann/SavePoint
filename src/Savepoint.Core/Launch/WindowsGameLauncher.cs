using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;
using Savepoint.Core.Platform;

namespace Savepoint.Core.Launch;

/// <summary>Starts games on Windows: programs without a shell, links through the shell.</summary>
public sealed class WindowsGameLauncher : IGameLauncher
{
    private readonly IProcessStarter _starter;
    private readonly ILaunchFiles _files;

    /// <summary>Creates a launcher that starts real processes.</summary>
    public WindowsGameLauncher()
        : this(new SystemProcessStarter(), new SystemLaunchFiles())
    {
    }

    internal WindowsGameLauncher(IProcessStarter starter, ILaunchFiles files)
    {
        _starter = starter;
        _files = files;
    }

    /// <inheritdoc />
    /// <remarks>
    /// The program starts with the shell off, so its arguments reach it exactly as given and Windows does not search for it.
    /// A program whose manifest demands administrator rights cannot be started this way and fails as a <see cref="LaunchSpawnException"/>.
    /// </remarks>
    public IGameProcess StartExecutable(string path, IReadOnlyList<string> args, string workingDirectory)
    {
        if (!_files.FileExists(path))
        {
            throw new PathMissingException(path, RepoRole.Install);
        }

        return Spawn.Program(_starter, new ProcessStartRequest(path, [.. args], workingDirectory, UseShellExecute: false));
    }

    /// <inheritdoc />
    public Task OpenUriAsync(string uri, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // The shell routes the link to whatever registered its scheme; there is no opener command to wait on.
        Spawn.Opener(_starter, new ProcessStartRequest(uri, [], null, UseShellExecute: true), uri)?.Dispose();
        return Task.CompletedTask;
    }
}
