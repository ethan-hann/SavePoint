using System.Globalization;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;
using Savepoint.Core.Platform;

namespace Savepoint.Core.Launch;

// What the Linux and macOS launchers share: they differ only in the opener command and in app bundles.
internal sealed class UnixLauncherCore
{
    private const string _shell = "/bin/sh";

    private readonly IProcessStarter _starter;
    private readonly ILaunchFiles _files;
    private readonly string _opener;
    private readonly TimeSpan _openerWait;
    private readonly TimeProvider _clock;

    public UnixLauncherCore(IProcessStarter starter, ILaunchFiles files, string opener, TimeSpan openerWait, TimeProvider clock)
    {
        _starter = starter;
        _files = files;
        _opener = opener;
        _openerWait = openerWait;
        _clock = clock;
    }

    // A missing file is reported before the other rules, since a moved install folder is what the player most needs to hear about.
    public IGameProcess StartFile(string path, IReadOnlyList<string> args, string workingDirectory)
    {
        if (!_files.FileExists(path))
        {
            throw new PathMissingException(path, RepoRole.Install);
        }

        if (path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            throw new LaunchWindowsProgramException(path);
        }

        // A script is read by the shell, so it needs no execute bit.
        if (path.EndsWith(".sh", StringComparison.OrdinalIgnoreCase))
        {
            return Spawn.Program(_starter, new ProcessStartRequest(_shell, [path, .. args], workingDirectory));
        }

        if (!_files.HasExecuteBit(path))
        {
            throw new LaunchSpawnException(
                LaunchMethod.Exe,
                $"'{path}' has no execute permission, and SavePoint does not change file permissions. Mark it executable and try again.",
                null);
        }

        return Spawn.Program(_starter, new ProcessStartRequest(path, [.. args], workingDirectory));
    }

    public async Task OpenLinkAsync(string uri, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var opener = Spawn.Opener(_starter, new ProcessStartRequest(_opener, [uri]), uri);
        if (opener is null)
        {
            return;
        }

        using var timeout = new CancellationTokenSource(_openerWait, _clock);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        try
        {
            await opener.WaitForExitAsync(stop.Token);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            // Some handlers keep the opener alive while they start; it has the link, so it is left running.
            return;
        }

        if (opener.ExitCode is { } code and not 0)
        {
            throw new LaunchSpawnException(
                LaunchMethod.Uri,
                string.Create(CultureInfo.InvariantCulture, $"'{_opener}' could not open '{uri}' (exit code {code})."),
                null);
        }
    }
}
