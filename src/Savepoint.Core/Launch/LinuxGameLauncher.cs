using Savepoint.Core.Platform;

namespace Savepoint.Core.Launch;

/// <summary>Starts games on Linux: native binaries and shell scripts directly, links through <c>xdg-open</c>.</summary>
public sealed class LinuxGameLauncher : IGameLauncher
{
    private static readonly TimeSpan _defaultOpenerWait = TimeSpan.FromSeconds(10);

    private readonly UnixLauncherCore _core;

    /// <summary>Creates a launcher that starts real processes.</summary>
    public LinuxGameLauncher()
        : this(new SystemProcessStarter(), new SystemLaunchFiles(), _defaultOpenerWait, TimeProvider.System)
    {
    }

    internal LinuxGameLauncher(IProcessStarter starter, ILaunchFiles files, TimeSpan openerWait, TimeProvider clock)
    {
        _core = new UnixLauncherCore(starter, files, "xdg-open", openerWait, clock);
    }

    /// <inheritdoc />
    public IGameProcess StartExecutable(string path, IReadOnlyList<string> args, string workingDirectory) =>
        _core.StartFile(path, args, workingDirectory);

    /// <inheritdoc />
    public Task OpenUriAsync(string uri, CancellationToken cancellationToken = default) => _core.OpenLinkAsync(uri, cancellationToken);
}
