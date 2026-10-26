using Savepoint.Core.Artwork;
using Savepoint.Core.Catalog;
using Savepoint.Core.Diagnostics;
using Savepoint.Core.Git;
using Savepoint.Core.Library;
using Savepoint.Core.Platform;
using Savepoint.Core.Services.Storage;

namespace Savepoint.Core.Backend;

/// <summary>
/// What <see cref="CoreBackend.Create"/> builds the backend from. Only <see cref="Launch"/> is required: every other member
/// defaults to the real machine, and tests replace the parts they need with temp folders and fakes.
/// </summary>
public sealed record BackendOptions
{
    /// <summary>The operating system's folders and tables; <see cref="PlatformFactory.ForCurrentOs"/> when null.</summary>
    public IPlatform? Platform { get; init; }

    /// <summary>Where the games, profiles and settings files live; the platform's catalog folder when null.</summary>
    public string? CatalogDirectory { get; init; }

    /// <summary>The folder SavePoint is installed in, which games may not overlap; the app's base directory when null.</summary>
    public string? InstallDirectory { get; init; }

    /// <summary>The bundled git; located next to the app (or at <c>SAVEPOINT_GIT_ROOT</c>) when null.</summary>
    public GitBinary? Git { get; init; }

    /// <summary>Free and total space of a volume; the real drives when null.</summary>
    public IDiskSpace? DiskSpace { get; init; }

    /// <summary>The clock for timestamps; the system clock when null.</summary>
    public TimeProvider? Time { get; init; }

    /// <summary>Tells the services which games are being played; nothing is reported running when null.</summary>
    public IGameSessions? Sessions { get; init; }

    /// <summary>
    /// Builds the launch component from the shared services; <see cref="Launch.Orchestration.LaunchOrchestrator.Factory"/> makes the
    /// real one. The backend attaches the sessions it returns and answers <see cref="CoreBackend.Launch"/> with its Api. Every
    /// shipped build has the orchestrator, so there is no backend without one.
    /// </summary>
    public required Func<BackendServices, LaunchComponents> Launch { get; init; }

    /// <summary>Finds the Steam client's folder, where artwork Steam already downloaded is read from; the real one for the platform when null (D30).</summary>
    public ISteamFolderLocator? SteamFolder { get; init; }

    /// <summary>Finds GOG Galaxy's image cache, where artwork Galaxy already downloaded is read from; the real one for the platform when null (D30). Tests pass a fake so none reads the real machine.</summary>
    public IGogGalaxyCacheLocator? GogGalaxyCache { get; init; }

    /// <summary>
    /// Reads what GOG's installer recorded for installed games; the Windows registry reader on Windows and nothing on other systems
    /// when null. Tests pass a fake so none reads the real machine.
    /// </summary>
    public IGogInstalls? GogInstalls { get; init; }

    /// <summary>
    /// The handler game artwork is fetched through from Steam's servers (D30). Null, the default, turns online lookup off whatever the
    /// setting says, so nothing built from default options ever reaches the network; only the app's startup supplies a real one. The
    /// handler must not follow redirects or keep cookies.
    /// </summary>
    public HttpMessageHandler? ArtworkHttp { get; init; }

    /// <summary>
    /// Where GOG Galaxy's database folder is looked for (the machine-wide application data folder, <c>C:\ProgramData</c>); the real one
    /// when null. Tests pass a folder of their own so none reads the real machine.
    /// </summary>
    public string? GalaxyProgramData { get; init; }

    /// <summary>Where Galaxy's <c>config.json</c> is looked for (the local application data folder); the real one when null. Tests pass a folder of their own.</summary>
    public string? GalaxyLocalAppData { get; init; }

    /// <summary>
    /// Reads the account id of the signed-in Steam user, which names the account's <c>userdata</c> folder; the Windows registry reader on
    /// Windows and nothing elsewhere when null. Tests pass a function of their own so none reads the real machine.
    /// </summary>
    public Func<uint?>? SteamActiveUser { get; init; }

    /// <summary>
    /// The folder copies of Galaxy's database are made in while it is read. A <c>savepoint-galaxy-copies</c> folder in the system's temp
    /// folder when null. Tests pass a folder of their own.
    /// </summary>
    public string? GalaxyTempFolder { get; init; }

    /// <summary>
    /// Where the backend records startup checks, failed calls, jobs, launch sessions, storage moves and failed git commands (D34);
    /// nothing is written when null. The backend shields itself from a log that throws.
    /// </summary>
    public IDiagnosticLog? Log { get; init; }

    /// <summary>How many loose objects in a repository make a capture queue an optimization (D13.17); 5000 by default.</summary>
    public int LooseObjectThreshold { get; init; } = 5000;

    /// <summary>Tuning for the git engine; its defaults when null. The clock is always replaced by <see cref="Time"/>.</summary>
    public GitEngineOptions? Engine { get; init; }

    // The templates a library scan matches games against; the embedded ones when null. Tests use it to match without the shipped files.
    internal IReadOnlyList<GameTemplate>? Templates { get; init; }

    // What the startup check does about a git a stopped SavePoint left running; tests give one with their own note and short waits.
    internal LeftoverGits? Leftovers { get; init; }

    // How long the startup check lets a leftover git end by itself before it is stopped; 10 seconds when null. Tests shorten it.
    internal TimeSpan? LeftoverWait { get; init; }

    // How long a git must run before it is written to the note of running git; half a second when null. Tests shorten it.
    internal TimeSpan? RunningGitNoteDelay { get; init; }

    // Called with the source and the destination after each file of a storage move is copied and before it is verified; tests use it to inject faults.
    internal Action<string, string>? AfterRelocationCopy { get; init; }

    // Called with the new root right after a storage move has written the settings; tests use it to make that write seem to fail.
    internal Action<string>? AfterRelocationSettings { get; init; }
}
