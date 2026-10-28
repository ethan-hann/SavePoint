using Savepoint.App.Tests.Fakes;
using Savepoint.Core.Api;
using Savepoint.Core.Artwork;
using Savepoint.Core.Backend;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;
using Savepoint.Core.Git;
using Savepoint.Core.Launch.Orchestration;
using Savepoint.Core.Platform;
using Savepoint.Core.Platform.Linux;
using Savepoint.Core.Platform.MacOS;
using Savepoint.Core.Platform.Windows;
using Savepoint.Core.Services.Storage;
using Savepoint.Testing;

namespace Savepoint.App.Tests.Contract;

// One backend the contract scenario drives: the fake or the real one, over the same game folders on disk.
internal abstract class ContractHarness : IAsyncDisposable
{
    protected ContractHarness()
    {
        Root = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "savepoint-contract-" + Guid.NewGuid().ToString("N")));
        Directory.CreateDirectory(Root);
    }

    public string Root { get; }

    public abstract IGamesApi Games { get; }

    public abstract IProfilesApi Profiles { get; }

    public abstract IHistoryApi History { get; }

    public abstract ISettingsApi Settings { get; }

    public abstract IJobsApi Jobs { get; }

    public abstract ILaunchApi Launch { get; }

    public abstract IArtworkApi Artwork { get; }

    public abstract IAppStatusApi AppStatus { get; }

    // The folder the storage lives in until settings name another, and the folder the catalog files live in.
    public abstract string DefaultStorageRoot { get; }

    public abstract string CatalogDirectory { get; }

    // Makes one catalog file unreadable, with a good last copy beside it or without one; returns the file's path.
    public abstract string MakeCatalogFileCorrupt(string fileName, bool withBackup);

    // Moves the storage folder, history and all, to <paramref name="movedTo"/>, as an unplugged drive or a renamed folder would leave the app.
    public abstract Task LoseStorageAsync(string movedTo, CancellationToken cancellationToken);

    // Puts Steam's artwork for an app where the lookup finds it: a file in the fake Steam folder for the real backend, and a scripted
    // find for the fake. Nothing reads it until look again.
    public abstract void SteamHasArt(GameDefinition game, int appId);

    // Returns once the lookup a read of the game's artwork started in the background has ended. The real backend runs it on its own thread, and
    // one that is still running when the story puts Steam's files in place could keep half of them, so the story waits for it first.
    public virtual Task ArtworkLookupDoneAsync(string gameId, CancellationToken cancellationToken) => Task.CompletedTask;

    // Returns once every event the calls so far raised has reached its subscribers. The real backend raises them before the call returns
    // (or from the thread a story already waits for), but the fake delivers them in the background, so a story that counts events waits here.
    public virtual Task EventsDeliveredAsync() => Task.CompletedTask;

    // Tells the backend which profile is about to be launched, so a fake can be scripted with what the real one works out for itself.
    public virtual void ExpectLaunch(GameDefinition game, string profileId)
    {
    }

    // Lets the game that StartAsync began run, and returns once the session says it does.
    public abstract Task GameRunningAsync(string gameId, CancellationToken cancellationToken);

    // Closes the running game, and returns once the session says it has exited.
    public abstract Task GameExitedAsync(string gameId, CancellationToken cancellationToken);

    // Ends the job the way the backend does: the fake is told to finish it, the real one is waited for.
    public abstract Task<JobSnapshot> FinishAsync(Guid jobId, CancellationToken cancellationToken);

    // Returns once the session has moved from saving to Completed; the fake moves with the job it was told to finish.
    public virtual Task WaitForCompletedAsync(string gameId, CancellationToken cancellationToken) => Task.CompletedTask;

    // Switches toward <paramref name="toProfileId"/> from the profile the folders are on and stops it part way, so the install folder is left half switched.
    public abstract Task InterruptSwitchAsync(GameDefinition game, string currentProfileId, string toProfileId, CancellationToken cancellationToken);

    // Starts a switch that does not play and returns its job once it is under way and cannot end until <see cref="ReleaseSwitch"/>.
    public abstract Task<Guid> HoldSwitchAsync(GameDefinition game, string toProfileId, CancellationToken cancellationToken);

    // Lets the switch started by <see cref="HoldSwitchAsync"/> go on; the story then waits for its job through <see cref="FinishAsync"/>.
    public virtual void ReleaseSwitch()
    {
    }

    // Tells the backend the half-switched folder has been finished, so a fake can be scripted with what the real one works out for itself.
    public virtual void SwitchFinished(GameDefinition game, string profileId)
    {
    }

    // The player saving during a session: a new file in the saves folder, which the real backend's save after play picks up.
    public static void PlayerSaves(GameDefinition game) =>
        File.WriteAllText(Path.Combine(game.External!.Path, "after-play.sav"), "progress made while playing");

    // A game with an install folder and a saves folder holding a few files.
    public GameDefinition NewGame(string name)
    {
        var id = name.ToLowerInvariant() + "-1";
        var install = Path.Combine(Root, "games", id, "game");
        var saves = Path.Combine(Root, "games", id, "saves");
        Directory.CreateDirectory(Path.Combine(install, "data"));
        Directory.CreateDirectory(saves);
        File.WriteAllText(Path.Combine(install, "game.exe"), "MZ not a real program");
        File.WriteAllText(Path.Combine(install, "data", "base.pak"), "base content");
        File.WriteAllText(Path.Combine(saves, "slot1.sav"), "save one");
        return new GameDefinition(
            Id: id,
            Name: name,
            Install: new InstallLocation(install),
            External: new ExternalLocation(saves),
            Launch: new LaunchConfig(LaunchMethod.Exe, "game.exe"));
    }

    // A game that keeps its mods outside the install folder, as 7 Days to Die does: the same game as <see cref="NewGame"/> plus a mods folder holding one mod.
    public GameDefinition NewGameWithMods(string name)
    {
        var game = NewGame(name);
        var mods = Path.Combine(Root, "games", game.Id, "mods");
        Directory.CreateDirectory(Path.Combine(mods, "CoolMod"));
        File.WriteAllText(Path.Combine(mods, "CoolMod", "ModInfo.xml"), "<xml />");
        return game with { Mods = new ModsLocation(mods) };
    }

    // A game folder holding the given program files, each of the given size, and nothing else; the game itself is not added to the library.
    public GameDefinition NewGameWithPrograms(string name, string folderName, params (string Path, int Bytes)[] programs)
    {
        var id = name.ToLowerInvariant() + "-1";
        var install = Path.Combine(Root, "games", id, folderName);
        Directory.CreateDirectory(install);
        foreach (var (path, bytes) in programs)
        {
            var full = Path.Combine(install, path);
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllBytes(full, new byte[bytes]);
        }

        ProgramsAre(install, [.. programs.Select(program => program.Path)]);
        return new GameDefinition(Id: id, Name: name, Install: new InstallLocation(install), Launch: new LaunchConfig(LaunchMethod.Exe, programs.Length > 0 ? programs[0].Path : "game.exe"));
    }

    // Tells the backend which programs an install folder holds, so a fake can be scripted with what the real one finds by scanning the disk.
    public virtual void ProgramsAre(string installPath, IReadOnlyList<string> programs)
    {
    }

    // Tells the backend a folder is not there yet, so a fake can be scripted with what the real one finds by looking at the disk.
    public virtual void FolderIsMissing(string path)
    {
    }

    public virtual async ValueTask DisposeAsync()
    {
        GC.SuppressFinalize(this);
        await Task.Run(DeleteRoot).ConfigureAwait(false);
    }

    // Git writes its object files read-only, which a plain recursive delete cannot remove on Windows.
    private void DeleteRoot()
    {
        foreach (var file in Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Directory.Delete(Root, recursive: true);
    }
}

internal sealed class FakeContractHarness : ContractHarness
{
    private readonly FakeBackend _backend;

    public FakeContractHarness()
    {
        // Events arrive after the call has returned and off the calling thread, as the real backend sends them.
        _backend = new FakeBackend(ScenarioBuilder.Empty()
            .WithDefaultStorageRoot(Path.Combine(Root, "home", "storage"))
            .WithCatalogDirectory(Path.Combine(Root, "home", "catalog")))
            .WithDelivery(EventDelivery.Background);
    }

    public override string DefaultStorageRoot => Path.Combine(Root, "home", "storage");

    public override string CatalogDirectory => Path.Combine(Root, "home", "catalog");

    public override IAppStatusApi AppStatus => _backend.AppStatus;

    // The fake has no disk to corrupt, so the startup answer is scripted to name the file the way the real check does.
    public override string MakeCatalogFileCorrupt(string fileName, bool withBackup)
    {
        var path = Path.Combine(CatalogDirectory, fileName);
        _backend.State.Startup = _backend.State.Startup with
        {
            Catalog = new CatalogStatus(CatalogDirectory, [new CorruptCatalogFile(path, withBackup ? path + ".bak" : null, ErrorInfo.From(new CatalogCorruptException(path, null)))]),
        };
        return path;
    }

    public override Task LoseStorageAsync(string movedTo, CancellationToken cancellationToken)
    {
        _backend.State.StorageFoldersWithHistory.Add(movedTo);
        _backend.WithStorageGone();
        return Task.CompletedTask;
    }

    public override IGamesApi Games => _backend.Games;

    public override IProfilesApi Profiles => _backend.Profiles;

    public override IHistoryApi History => _backend.History;

    public override ISettingsApi Settings => _backend.Settings;

    public override IJobsApi Jobs => _backend.Jobs;

    public override ILaunchApi Launch => _backend.Launch;

    public override IArtworkApi Artwork => _backend.Artwork;

    public override void SteamHasArt(GameDefinition game, int appId)
    {
        _ = appId;
        var wide = TestImages.Write(Path.Combine(Root, "fake-steam"), "header.jpg", TestImages.Jpeg(460, 215));
        var square = TestImages.Write(Path.Combine(Root, "fake-steam"), "library_600x900.jpg", TestImages.Jpeg(600, 900));
        _backend.Artwork.ScriptSteamFinds(game.Id, wide, square);
    }

    public override void FolderIsMissing(string path) => _backend.State.MissingFolders.Add(path);

    public override Task EventsDeliveredAsync() => _backend.WhenEventsDeliveredAsync();

    public override void ProgramsAre(string installPath, IReadOnlyList<string> programs) => _backend.State.Executables[installPath] = programs;

    // The preview the real backend gave for a profile whose folders hold it (Fakes/Previews/profile-tip-clean.json).
    public override void ExpectLaunch(GameDefinition game, string profileId) =>
        _backend.State.LaunchPreviews[(game.Id, new LaunchTarget.ProfileTip(profileId))] =
            LaunchPreviewFixtures.Load("profile-tip-clean", new PreviewNames(game.Id, profileId, game.Install.Path, game.External?.Path));

    public override Task GameRunningAsync(string gameId, CancellationToken cancellationToken)
    {
        _backend.Launch.ReportRunning(gameId);
        return Task.CompletedTask;
    }

    public override Task GameExitedAsync(string gameId, CancellationToken cancellationToken)
    {
        _backend.Launch.ReportExited(gameId, TimeSpan.FromMinutes(5), newFileCount: 0);
        return Task.CompletedTask;
    }

    public override Task<JobSnapshot> FinishAsync(Guid jobId, CancellationToken cancellationToken)
    {
        _backend.Jobs.Complete(jobId);
        return Task.FromResult(_backend.Jobs.Get(jobId)!);
    }

    // The fake's job stays running until the story finishes it.
    public override async Task<Guid> HoldSwitchAsync(GameDefinition game, string toProfileId, CancellationToken cancellationToken)
    {
        var started = await _backend.Launch.SwitchAsync(game.Id, new LaunchTarget.ProfileTip(toProfileId)).ConfigureAwait(false);
        return started.Ok ? started.Value.JobId : throw new InvalidOperationException("The switch to hold did not start: " + started.Error.MessageId);
    }

    // Plays what the real backend gave for a folder a stopped switch left half done: dirty, unfinished, and the target that finishes it
    // (Fakes/Previews/stopped-switch.json). The switch is stopped the way the real one is, by asking the job to cancel and then ending it.
    public override async Task InterruptSwitchAsync(GameDefinition game, string currentProfileId, string toProfileId, CancellationToken cancellationToken)
    {
        var started = await _backend.Launch.StartAsync(game.Id, new LaunchTarget.ProfileTip(toProfileId));
        _backend.Jobs.CompleteCancelled(started.Value!.JobId);
        _backend.State.LaunchPreviews[(game.Id, new LaunchTarget.ProfileTip(currentProfileId))] =
            LaunchPreviewFixtures.Load("stopped-switch", new PreviewNames(game.Id, currentProfileId, game.Install.Path, game.External?.Path, toProfileId));
    }

    public override void SwitchFinished(GameDefinition game, string profileId) =>
        _backend.State.LaunchPreviews[(game.Id, new LaunchTarget.ProfileTip(profileId))] =
            LaunchPreviewFixtures.Load("finished-switch", new PreviewNames(game.Id, profileId, game.Install.Path, game.External?.Path));
}

internal sealed class RealContractHarness : ContractHarness
{
    private static readonly TimeSpan _limit = TimeSpan.FromMinutes(5);

    private readonly CoreBackend _backend;
    private readonly ScriptedGame _game = new();
    private readonly string _steamFolder;
    private readonly GatedDisk _disk = new();

    public RealContractHarness()
    {
        _steamFolder = Path.Combine(Root, "steam");
        var home = Path.Combine(Root, "home");
        var install = Path.Combine(Root, "app");
        Directory.CreateDirectory(install);
        _backend = CoreBackend.Create(new BackendOptions
        {
            Platform = HostPlatform(home),
            InstallDirectory = install,
            Git = GitBinary.Locate(Path.Combine(Root, "gitconfig")),
            LooseObjectThreshold = int.MaxValue,
            SteamFolder = new FolderLocator(_steamFolder),
            GogGalaxyCache = new NoGalaxyCache(),
            SteamActiveUser = () => null,
            GalaxyProgramData = Path.Combine(Root, "galaxy", "program-data"),
            GalaxyLocalAppData = Path.Combine(Root, "galaxy", "local-app-data"),
            GalaxyTempFolder = Path.Combine(Root, "galaxy", "copies"),
            DiskSpace = _disk,
            Launch = LaunchOrchestrator.Factory(new LaunchOrchestratorOptions { Starter = _game.Starter(TimeProvider.System) }),
        });
    }

    public override IGamesApi Games => _backend.Games;

    public override IProfilesApi Profiles => _backend.Profiles;

    public override IHistoryApi History => _backend.History;

    public override ISettingsApi Settings => _backend.Settings;

    public override IJobsApi Jobs => _backend.Jobs;

    public override ILaunchApi Launch => _backend.Launch;

    public override IArtworkApi Artwork => _backend.Artwork;

    public override IAppStatusApi AppStatus => _backend.AppStatus;

    public override string DefaultStorageRoot => _backend.Services.Platform.Directories.DefaultStorageRoot;

    public override string CatalogDirectory => _backend.Services.Catalog.DirectoryPath;

    public override string MakeCatalogFileCorrupt(string fileName, bool withBackup)
    {
        var path = Path.Combine(CatalogDirectory, fileName);
        Directory.CreateDirectory(CatalogDirectory);
        File.WriteAllText(path, "{ this is not a catalog");
        if (withBackup)
        {
            File.WriteAllText(path + ".bak", fileName == CatalogStore.SettingsFileName ? "{}" : "[]");
        }

        return path;
    }

    // Work queued behind a call, such as the compaction after a save, would still be reading the folder, so it ends first.
    public override async Task LoseStorageAsync(string movedTo, CancellationToken cancellationToken)
    {
        var until = DateTime.UtcNow + _limit;
        while (_backend.Jobs.List().Any(job => job.Status == JobStatus.Running) && DateTime.UtcNow < until)
        {
            await Task.Delay(25, cancellationToken).ConfigureAwait(false);
        }

        var settings = await _backend.Settings.GetAsync(cancellationToken).ConfigureAwait(false);
        var root = settings.Ok ? settings.Value.StorageRoot : throw new InvalidOperationException("The settings could not be read: " + settings.Error.MessageId);
        Directory.CreateDirectory(Path.GetDirectoryName(movedTo)!);
        Directory.Move(root, movedTo);
    }

    public override void SteamHasArt(GameDefinition game, int appId)
    {
        _ = game;
        var cache = Path.Combine(_steamFolder, "appcache", "librarycache", appId.ToString(System.Globalization.CultureInfo.InvariantCulture));
        TestImages.Write(cache, "header.jpg", TestImages.Jpeg(460, 215));
        TestImages.Write(cache, "library_600x900.jpg", TestImages.Jpeg(600, 900));
    }

    // A lookup holds its game's gate from the read that starts it until it ends, so taking the gate waits for it.
    public override async Task ArtworkLookupDoneAsync(string gameId, CancellationToken cancellationToken)
    {
        var gate = ((ArtworkApi)_backend.Artwork).LookupGateOf(gameId);
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        gate.Release();
    }

    public override Task GameRunningAsync(string gameId, CancellationToken cancellationToken) =>
        WaitForSessionAsync(gameId, state => state is SessionState.Running, cancellationToken);

    public override Task GameExitedAsync(string gameId, CancellationToken cancellationToken)
    {
        _game.ExitAll();
        return WaitForSessionAsync(gameId, state => state is SessionState.Exited, cancellationToken);
    }

    public override Task WaitForCompletedAsync(string gameId, CancellationToken cancellationToken) =>
        WaitForSessionAsync(gameId, state => state is SessionState.Completed, cancellationToken);

    // Starts the switch with the install folder held at the free-space question of its own switch, then cancels it there: nothing is written, and the folder is marked unfinished.
    public override async Task InterruptSwitchAsync(GameDefinition game, string currentProfileId, string toProfileId, CancellationToken cancellationToken)
    {
        // The checks before the switch ask about the install folder too, and how many they ask depends on how the folders share drives,
        // so the hold waits for the job to say it is switching the install folder, as a job that began after this call.
        var before = _backend.Jobs.List().Select(job => job.JobId).ToHashSet();
        _disk.Hold(game.Install.Path, () => InstallSwitchBegun(game.Id, before));
        try
        {
            var started = await _backend.Launch.StartAsync(game.Id, new LaunchTarget.ProfileTip(toProfileId)).ConfigureAwait(false);
            if (!started.Ok)
            {
                throw new InvalidOperationException("The switch to interrupt did not start: " + started.Error.MessageId);
            }

            await _disk.ReachedAsync(cancellationToken).ConfigureAwait(false);
            await _backend.Jobs.CancelAsync(started.Value.JobId).ConfigureAwait(false);
            _disk.Release();
            await FinishAsync(started.Value.JobId, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _disk.Release();
        }
    }

    private bool InstallSwitchBegun(string gameId, HashSet<Guid> jobsBefore) =>
        _backend.Jobs.List().Any(job => job.GameId == gameId
            && !jobsBefore.Contains(job.JobId)
            && job.RecentLines.OfType<JobLine.Step>().Any(step => step.MessageId == JobSteps.LaunchSwitchFolder && Equals(step.Args["folder"], "install")));

    // The switch is held at the install folder's first free-space question, with the game claimed.
    public override async Task<Guid> HoldSwitchAsync(GameDefinition game, string toProfileId, CancellationToken cancellationToken)
    {
        _disk.Hold(game.Install.Path);
        var started = await _backend.Launch.SwitchAsync(game.Id, new LaunchTarget.ProfileTip(toProfileId)).ConfigureAwait(false);
        if (!started.Ok)
        {
            _disk.Release();
            throw new InvalidOperationException("The switch to hold did not start: " + started.Error.MessageId);
        }

        await _disk.ReachedAsync(cancellationToken).ConfigureAwait(false);
        return started.Value.JobId;
    }

    public override void ReleaseSwitch() => _disk.Release();

    // The real session moves on its own threads, so the story waits for the phase it needs.
    private async Task WaitForSessionAsync(string gameId, Func<SessionState, bool> reached, CancellationToken cancellationToken)
    {
        var until = DateTime.UtcNow + _limit;
        while (_backend.Launch.GetSession(gameId) is not { } session || !reached(session.State))
        {
            if (DateTime.UtcNow > until)
            {
                throw new TimeoutException($"The session never reached the phase the story waits for; it is {_backend.Launch.GetSession(gameId)?.State.GetType().Name ?? "missing"}.");
            }

            await Task.Delay(20, cancellationToken).ConfigureAwait(false);
        }
    }

    public override async Task<JobSnapshot> FinishAsync(Guid jobId, CancellationToken cancellationToken)
    {
        var finished = new TaskCompletionSource<JobSnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        void OnChanged(object? sender, JobSnapshot snapshot)
        {
            if (snapshot.JobId == jobId && snapshot.Status != JobStatus.Running)
            {
                finished.TrySetResult(snapshot);
            }
        }

        _backend.Jobs.JobChanged += OnChanged;
        try
        {
            if (_backend.Jobs.Get(jobId) is { Status: not JobStatus.Running } already)
            {
                return already;
            }

            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            deadline.CancelAfter(_limit);
            await using var registration = deadline.Token.Register(() => finished.TrySetCanceled());
            return await finished.Task.ConfigureAwait(false);
        }
        finally
        {
            _backend.Jobs.JobChanged -= OnChanged;
        }
    }

    public override async ValueTask DisposeAsync()
    {
        // Work queued behind a call, such as the cleanup after a profile delete, must end before its folders go.
        var until = DateTime.UtcNow + _limit;
        while (_backend.Jobs.List().Any(job => job.Status == JobStatus.Running) && DateTime.UtcNow < until)
        {
            await Task.Delay(25).ConfigureAwait(false);
        }

        _backend.Dispose();
        await base.DisposeAsync().ConfigureAwait(false);
    }

    // The running OS's platform over a home inside the throwaway folder, so nothing real is touched.
    private static IPlatform HostPlatform(string home)
    {
        var environment = new PlatformEnvironment { HomeDirectory = home };
        return PlatformFactory.CurrentOs switch
        {
            OsKind.Windows => new WindowsPlatform(environment),
            OsKind.Linux => new LinuxPlatform(environment),
            _ => new MacOSPlatform(environment),
        };
    }
}

// A Steam folder that is just a folder the harness fills; the real machine's Steam is never read.
internal sealed class FolderLocator(string folder) : ISteamFolderLocator
{
    public string? FindSteamFolder() => Directory.Exists(folder) ? folder : null;
}

// A machine with no GOG Galaxy; the real machine's Galaxy cache is never read.
internal sealed class NoGalaxyCache : IGogGalaxyCacheLocator
{
    public string? FindWebcacheFolder() => null;
}

// Real free space, except that one folder can be held at its first question until released: how the story stops a switch part way.
internal sealed class GatedDisk : IDiskSpace
{
    private static readonly TimeSpan _limit = TimeSpan.FromSeconds(60);

    private readonly DiskSpace _real = new();
    private readonly ManualResetEventSlim _reached = new();
    private readonly ManualResetEventSlim _release = new(initialState: true);
    private volatile string? _folder;
    private volatile Func<bool>? _ready;

    // The folder is held at its first question once `ready` is true (no condition: its first question). A launch asks about a folder
    // more than once before its switch writes, and how often depends on which folders share a drive, so a switch that must be stopped
    // inside the folder's own switch names that moment instead of counting questions.
    public void Hold(string folder, Func<bool>? ready = null)
    {
        _reached.Reset();
        _release.Reset();
        _ready = ready;
        _folder = folder;
    }

    public void Release()
    {
        _folder = null;
        _release.Set();
    }

    public Task ReachedAsync(CancellationToken cancellationToken) =>
        Task.Run(() => _reached.Wait(_limit, cancellationToken) ? true : throw new TimeoutException("The switch never reached the folder it was to be stopped at."), cancellationToken);

    public long? GetFreeBytes(string path)
    {
        if (_folder is { } held && path.StartsWith(held, StringComparison.OrdinalIgnoreCase) && !_release.IsSet && (_ready?.Invoke() ?? true))
        {
            _reached.Set();
            _release.Wait(_limit);
        }

        return _real.GetFreeBytes(path);
    }

    public long? GetTotalBytes(string path) => _real.GetTotalBytes(path);

    public string? GetVolumeId(string path) => _real.GetVolumeId(path);
}
