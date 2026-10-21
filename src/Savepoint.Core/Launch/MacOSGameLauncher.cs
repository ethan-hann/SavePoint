using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;
using Savepoint.Core.Platform;

namespace Savepoint.Core.Launch;

/// <summary>Starts games on macOS: app bundles through <c>open</c>, bare executables and scripts directly, links through <c>open</c>.</summary>
public sealed class MacOSGameLauncher : IGameLauncher
{
    private const string _open = "/usr/bin/open";

    private static readonly TimeSpan _defaultOpenerWait = TimeSpan.FromSeconds(10);

    private readonly IProcessStarter _starter;
    private readonly ILaunchFiles _files;
    private readonly UnixLauncherCore _core;

    /// <summary>Creates a launcher that starts real processes.</summary>
    public MacOSGameLauncher()
        : this(new SystemProcessStarter(), new SystemLaunchFiles(), _defaultOpenerWait, TimeProvider.System)
    {
    }

    internal MacOSGameLauncher(IProcessStarter starter, ILaunchFiles files, TimeSpan openerWait, TimeProvider clock)
    {
        _starter = starter;
        _files = files;
        _core = new UnixLauncherCore(starter, files, _open, openerWait, clock);
    }

    /// <inheritdoc />
    /// <remarks>
    /// An <c>.app</c> bundle starts as <c>open -W -n</c>, so the returned process is the <c>open</c> command, which lives until
    /// the app quits; the app itself does not inherit the working directory. A command that fails at once exits with a non-zero
    /// <see cref="IGameProcess.ExitCode"/> rather than throwing. Anything else follows the Linux rules.
    /// </remarks>
    public IGameProcess StartExecutable(string path, IReadOnlyList<string> args, string workingDirectory)
    {
        var bundle = path.TrimEnd('/');
        if (!bundle.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
        {
            return _core.StartFile(path, args, workingDirectory);
        }

        if (!_files.DirectoryExists(bundle))
        {
            throw new PathMissingException(bundle, RepoRole.Install);
        }

        IReadOnlyList<string> open = args.Count == 0 ? ["-W", "-n", bundle] : ["-W", "-n", bundle, "--args", .. args];
        return Spawn.Program(_starter, new ProcessStartRequest(_open, open, workingDirectory, UseShellExecute: false));
    }

    /// <inheritdoc />
    public Task OpenUriAsync(string uri, CancellationToken cancellationToken = default) => _core.OpenLinkAsync(uri, cancellationToken);
}
