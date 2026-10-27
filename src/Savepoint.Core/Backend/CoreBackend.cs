using Savepoint.Core.Api;
using Savepoint.Core.Artwork;
using Savepoint.Core.Catalog;
using Savepoint.Core.CloudSaves;
using Savepoint.Core.Diagnostics;
using Savepoint.Core.Git;
using Savepoint.Core.Library;
using Savepoint.Core.Platform;
using Savepoint.Core.Platform.Windows;
using Savepoint.Core.Services.AppStatus;
using Savepoint.Core.Services.Games;
using Savepoint.Core.Services.History;
using Savepoint.Core.Services.Jobs;
using Savepoint.Core.Services.Locking;
using Savepoint.Core.Services.Profiles;
using Savepoint.Core.Services.Settings;
using Savepoint.Core.Services.Storage;

namespace Savepoint.Core.Backend;

/// <summary>
/// The composition root of Core: every Api the UI uses, wired over one platform, one catalog, one bundled git, one job
/// registry and one game lock. Create it once at startup; nothing here is rebuilt for the life of the process.
/// </summary>
/// <remarks>
/// Creating the backend does not touch the storage root, the catalog or git: those are checked by
/// <see cref="IAppStatusApi.CheckAsync"/>, so a missing git or a corrupt catalog file reaches the user as a startup screen
/// instead of a crash. A move of the storage root rebuilds only the path judge and the engine (D13.4); the job registry, the
/// game lock and the session state are untouched.
/// </remarks>
public sealed class CoreBackend
{
    private CoreBackend(
        BackendServices services,
        IGamesApi games,
        IProfilesApi profiles,
        ILaunchApi launch,
        ISettingsApi settings,
        IHistoryApi history,
        IAppStatusApi appStatus,
        IArtworkApi artwork)
    {
        Services = services;
        Games = games;
        Profiles = profiles;
        Launch = launch;
        Settings = settings;
        History = history;
        AppStatus = appStatus;
        Artwork = artwork;
    }

    /// <summary>The shared pieces the Apis are built from, for components such as the launch orchestrator.</summary>
    public BackendServices Services { get; }

    /// <summary>The games Api.</summary>
    public IGamesApi Games { get; }

    /// <summary>The profiles Api.</summary>
    public IProfilesApi Profiles { get; }

    /// <summary>The launch Api, which is the launch component's.</summary>
    public ILaunchApi Launch { get; }

    /// <summary>The settings Api.</summary>
    public ISettingsApi Settings { get; }

    /// <summary>The history Api.</summary>
    public IHistoryApi History { get; }

    /// <summary>The jobs Api, which is the job registry.</summary>
    public IJobsApi Jobs => Services.Jobs;

    /// <summary>The startup checks and catalog recovery.</summary>
    public IAppStatusApi AppStatus { get; }

    /// <summary>The artwork Api: the user's own image, Steam's images, or none for each game (D30).</summary>
    public IArtworkApi Artwork { get; }

    // Galaxy exists on Windows only here; elsewhere its games read as unknown.
    private static GalaxyCloudReader? GalaxyReader(BackendOptions options) =>
        OperatingSystem.IsWindows()
            ? new GalaxyCloudReader(
                options.GalaxyProgramData ?? Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                options.GalaxyLocalAppData ?? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                options.GalaxyTempFolder ?? Path.Combine(Path.GetTempPath(), "savepoint-galaxy-copies"),
                timeProvider: options.Time)
            : null;

    /// <summary>Builds the backend.</summary>
    /// <param name="options">What to build it from; every member but the launch component defaults to the real machine.</param>
    /// <exception cref="InvalidOperationException">The account has no home folder.</exception>
    /// <exception cref="IOException">The folder for git's configuration cannot be written.</exception>
    public static CoreBackend Create(BackendOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        var platform = options.Platform ?? PlatformFactory.ForCurrentOs();
        var time = options.Time ?? TimeProvider.System;
        var log = new GuardedDiagnosticLog(options.Log ?? DiagnosticLog.None);
        var catalog = new CatalogStore(options.CatalogDirectory ?? platform.Directories.CatalogDir, time);
        var git = options.Git ?? GitBinary.Locate(Path.Combine(platform.Directories.RuntimeDir, "git"));
        var noteFile = RunningGitNote.PathIn(platform.Directories.RuntimeDir);
        var registry = GitProcessRegistry.ForApp(noteFile, options.RunningGitNoteDelay, log);
        var leftovers = options.Leftovers ?? new LeftoverGits(noteFile, log, registry, options.LeftoverWait);
        var runner = new GitRunner(git, new GitRunnerOptions { Log = log, Registry = registry });
        var disk = options.DiskSpace ?? new DiskSpace();
        var installDirectory = Path.TrimEndingDirectorySeparator(Path.GetFullPath(options.InstallDirectory ?? AppContext.BaseDirectory));
        var engineOptions = (options.Engine ?? new GitEngineOptions()) with { Clock = time, Log = log, LeftoverGitRunning = leftovers.AnyRunning };

        var sessions = new ForwardingGameSessions();
        if (options.Sessions is { } given)
        {
            sessions.Attach(given);
        }

        var services = new BackendServices(
            platform,
            catalog,
            git,
            runner,
            new JobRegistry(time, log),
            new GameLock(),
            sessions,
            new StorageHost(catalog, platform, runner, disk, installDirectory, engineOptions),
            disk,
            time,
            installDirectory,
            options.LooseObjectThreshold,
            log,
            leftovers);

        var optimization = new OptimizationService(services);
        var launch = options.Launch(services);
        sessions.Attach(launch.Sessions);

        var steam = options.SteamFolder ?? SteamFolderLocator.ForPlatform(platform);
        var gog = options.GogInstalls ?? (OperatingSystem.IsWindows() ? new WindowsGogRegistry() : null);
        var sources = new LibrarySources(steam, gog, options.Templates ?? GameTemplates.All);
        var cloudSaves = new CloudSaveLookup(new SteamCloudReader(steam, options.SteamActiveUser ?? (OperatingSystem.IsWindows() ? WindowsSteamRegistry.ReadActiveUser : null)), GalaxyReader(options));

        return new CoreBackend(
            services,
            new GamesApi(services, optimization, sources, cloudSaves),
            new ProfilesApi(services, optimization),
            launch.Api,
            new SettingsApi(services, optimization, new StorageRelocator(services, options.AfterRelocationCopy, options.AfterRelocationSettings), new DataClearer(services)),
            new HistoryApi(services),
            new AppStatusApi(services, options.AfterRelocationSettings),
            new ArtworkApi(services, steam, options.GogGalaxyCache ?? GogGalaxyCacheLocator.ForPlatform(platform), options.ArtworkHttp));
    }
}
