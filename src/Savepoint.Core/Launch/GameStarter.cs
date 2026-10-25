using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;
using Savepoint.Core.Paths;
using Savepoint.Core.Platform;

namespace Savepoint.Core.Launch;

/// <summary>Starts a game from its launch settings and hands back the watcher that says when it stopped.</summary>
public sealed class GameStarter
{
    private readonly IGameLauncher _launcher;
    private readonly IProcessInspector _processes;
    private readonly OsKind _os;
    private readonly PathSyntax _paths;
    private readonly RunningGameCheck _check;
    private readonly GameSessionOptions _options;
    private readonly TimeProvider _clock;

    /// <summary>Creates a starter from its parts.</summary>
    /// <param name="launcher">What starts programs and opens links.</param>
    /// <param name="processes">Where running processes come from, for exit detection.</param>
    /// <param name="os">The OS the processes come from; it decides how paths and process names are compared.</param>
    /// <param name="options">Polling interval and grace for every session this starter starts.</param>
    /// <param name="clock">Time source for play durations and waits.</param>
    /// <exception cref="ArgumentOutOfRangeException">The polling interval is not positive, or the grace or the relaunch window is negative.</exception>
    public GameStarter(IGameLauncher launcher, IProcessInspector processes, OsKind os, GameSessionOptions options, TimeProvider clock)
    {
        options.EnsureValid();
        _launcher = launcher;
        _processes = processes;
        _os = os;
        _paths = PathSyntax.For(os);
        _check = new RunningGameCheck(processes, os);
        _options = options;
        _clock = clock;
    }

    /// <summary>Creates a starter that uses the real launcher for a platform's OS and its process list.</summary>
    /// <param name="platform">The platform to start games on.</param>
    /// <param name="options">Timings; the defaults when null.</param>
    /// <param name="clock">Time source; the system clock when null.</param>
    public static GameStarter For(IPlatform platform, GameSessionOptions? options = null, TimeProvider? clock = null) =>
        new(GameLaunchers.ForOs(platform.Os), platform.Processes, platform.Os, options ?? new GameSessionOptions(), clock ?? TimeProvider.System);

    /// <summary>True when the process named in <paramref name="launch"/> is running, for the check before a launch.</summary>
    /// <param name="launch">The game's launch settings; a game with no process name cannot be recognized and answers false.</param>
    public bool IsRunning(LaunchConfig launch) =>
        ProcessNameOf(launch) is { } name && _check.IsRunning(name);

    /// <summary>Starts the game and begins following the session.</summary>
    /// <param name="launch">The game's launch settings.</param>
    /// <param name="installFolder">The game's install folder as an absolute path; a program's path is resolved against it and it is the working directory.</param>
    /// <param name="cancellationToken">Stops waiting for an OS link opener; a program that already started keeps running.</param>
    /// <returns>The watcher for the session; the caller owns it and disposes it when the session record goes away.</returns>
    /// <exception cref="ArgumentException">A program is to be started and <paramref name="installFolder"/> is not an absolute path.</exception>
    /// <exception cref="LaunchSpawnException">The settings are unusable, or the OS refused to start the game.</exception>
    /// <exception cref="PathMissingException">The program or app bundle is not in the install folder.</exception>
    /// <exception cref="LaunchWindowsProgramException">The program is a Windows .exe and this OS cannot start one directly.</exception>
    public async Task<GameSessionWatcher> StartAsync(LaunchConfig launch, string installFolder, CancellationToken cancellationToken = default)
    {
        var processName = ProcessNameOf(launch);
        if (launch.Method == LaunchMethod.Uri)
        {
            if (string.IsNullOrWhiteSpace(launch.Uri))
            {
                throw new LaunchSpawnException(LaunchMethod.Uri, "The game's launch settings give no link to open.", null);
            }

            // The watcher starts only once the OS has the link, so a failed open leaves no session behind.
            await _launcher.OpenUriAsync(launch.Uri, cancellationToken);
            return new GameSessionWatcher(null, processName, _processes, _os, _options, _clock);
        }

        var path = ResolveProgram(launch.ExePath, installFolder);
        var child = _launcher.StartExecutable(path, launch.Args, installFolder);
        return new GameSessionWatcher(child, processName, _processes, _os, _options, _clock, installFolder);
    }

    private static string? ProcessNameOf(LaunchConfig launch) =>
        string.IsNullOrWhiteSpace(launch.ProcessNameForExitDetection) ? null : launch.ProcessNameForExitDetection.Trim();

    // The catalog already refuses an absolute or escaping path; a definition that got past it must still not start anything outside the install folder.
    private string ResolveProgram(string? exePath, string installFolder)
    {
        if (string.IsNullOrWhiteSpace(exePath))
        {
            throw new LaunchSpawnException(LaunchMethod.Exe, "The game's launch settings name no program to start.", null);
        }

        var root = _paths.Normalize(installFolder);
        var inside = root.EndsWith(_paths.Separator) ? root : root + _paths.Separator;
        if (_paths.IsAbsolute(exePath) || exePath[0] is '/' or '\\' || !_paths.Combine(installFolder, exePath).StartsWith(inside, _paths.Comparison))
        {
            throw new LaunchSpawnException(LaunchMethod.Exe, $"'{exePath}' is not inside the install folder '{installFolder}'.", null);
        }

        return _paths.Combine(installFolder, exePath);
    }
}
