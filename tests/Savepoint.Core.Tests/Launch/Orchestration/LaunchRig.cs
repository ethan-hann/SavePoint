using System.Diagnostics;
using Savepoint.Core.Api;
using Savepoint.Core.Backend;
using Savepoint.Core.Catalog;
using Savepoint.Core.Git;
using Savepoint.Core.Launch;
using Savepoint.Core.Launch.Orchestration;
using Savepoint.Core.Platform;
using Savepoint.Core.Services;
using Savepoint.Core.Tests.Backend;
using Savepoint.Core.Tests.Services.Exe;

namespace Savepoint.Core.Tests.Launch.Orchestration;

// A whole backend with the real orchestrator, over a game whose install folder holds a private copy of the stand-in game (under a
// process name no other test shares) and whose saves folder is separate. Disposing kills whatever is still running under that name.
internal sealed class LaunchRig : IDisposable
{
    // Every wait in these tests is bounded: a launch that hangs must fail its test, not hold the machine.
    private static readonly TimeSpan _patience = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan _jobPatience = TimeSpan.FromSeconds(90);

    private readonly object _gate = new();
    private readonly List<LaunchSessionInfo> _events = [];

    private readonly TwoProfileTemplates.Slot? _slot;
    private int _disposed;

    private LaunchRig(BackendWorkspace workspace, GameDefinition game, string exeName, TwoProfileTemplates.Slot? slot = null)
    {
        Workspace = workspace;
        Game = game;
        ExeName = exeName;
        _slot = slot;
        Launch.SessionChanged += OnSessionChanged;
    }

    public BackendWorkspace Workspace { get; }

    // The game as the catalog holds it; a saves folder added later through AddSavesFolderAsync replaces it.
    public GameDefinition Game { get; private set; }

    // At most eight characters, so the kernel's 15-character process name keeps it whole.
    public string ExeName { get; }

    public ILaunchApi Launch => Workspace.Backend.Launch;

    public IProfilesApi Profiles => Workspace.Profiles;

    public IHistoryApi History => Workspace.History;

    public IJobsApi Jobs => Workspace.Jobs;

    public string InstallPath => Game.Install.Path;

    public string SavesPath => Game.External!.Path;

    public string ReadyFile => Path.Combine(Workspace.Root, "ready.txt");

    public CancellationToken Token => TestContext.Current.CancellationToken;

    // Every event the session stream has raised so far, oldest first.
    public IReadOnlyList<LaunchSessionInfo> Events
    {
        get
        {
            lock (_gate)
            {
                return [.. _events];
            }
        }
    }

    // The game runs until it is killed unless the arguments say otherwise, and writes its saves into the saves folder.
    public static async Task<LaunchRig> CreateAsync(
        Func<BackendOptions, BackendOptions>? configure = null,
        IReadOnlyList<string>? gameArguments = null,
        IReadOnlyList<string>? modHints = null,
        string? exePath = null,
        bool followProcessName = false,
        bool withSaves = true,
        string? processName = null,
        GameSessionOptions? session = null,
        GameStarter? starter = null,
        ISteamClient? steam = null,
        SteamWaitOptions? steamWait = null)
    {
        var exeName = "fg" + Guid.NewGuid().ToString("N")[..6];
        var exeFile = exeName + (OperatingSystem.IsWindows() ? ".exe" : string.Empty);
        var workspace = new BackendWorkspace(WithOrchestrator(configure, session, starter, steam, steamWait));
        try
        {
            var definition = workspace.NewGame("Play", withSaves, modHints ?? ["mods"]);
            InstallStandInGame(definition.Install.Path, exeFile);
            var arguments = new List<string>();
            if (withSaves)
            {
                arguments.AddRange(["--saves", definition.External!.Path]);
            }

            arguments.AddRange(["--ready-file", Path.Combine(workspace.Root, "ready.txt")]);
            arguments.AddRange(gameArguments ?? []);
            var game = definition with { Launch = new LaunchConfig(LaunchMethod.Exe, exePath ?? exeFile, null, arguments, processName ?? (followProcessName ? exeName : null)) };
            return new LaunchRig(workspace, await workspace.AddGameAsync(game), exeName);
        }
        catch
        {
            workspace.Dispose();
            throw;
        }
    }

    // The backend options every rig builds: the real orchestrator, polling fast so a test does not wait on the production interval.
    // The Steam client is never the machine's own: a rig that does not script one gets a closed client.
    private static Func<BackendOptions, BackendOptions> WithOrchestrator(
        Func<BackendOptions, BackendOptions>? configure,
        GameSessionOptions? session,
        GameStarter? starter,
        ISteamClient? steam = null,
        SteamWaitOptions? steamWait = null)
    {
        var rigSession = session ?? new GameSessionOptions { PollInterval = TimeSpan.FromMilliseconds(100), Grace = TimeSpan.FromSeconds(3) };
        var rigSteamWait = steamWait ?? new SteamWaitOptions { Timeout = TimeSpan.FromSeconds(2), PollInterval = TimeSpan.FromMilliseconds(20) };
        return options =>
        {
            var launching = options with { Launch = LaunchOrchestrator.Factory(new LaunchOrchestratorOptions { Session = rigSession, Starter = starter, Steam = steam ?? new FakeSteamClient(), SteamWait = rigSteamWait }) };
            return configure is null ? launching : configure(launching);
        };
    }

    // The tool's own output folder holds the program and what it needs to start; the program is renamed on the way in.
    private static void InstallStandInGame(string install, string exeFile)
    {
        var source = Path.GetDirectoryName(FakeGameProcess.ExecutablePath)!;
        foreach (var file in Directory.GetFiles(source))
        {
            File.Copy(file, Path.Combine(install, Path.GetFileName(file)), overwrite: true);
        }

        File.Move(Path.Combine(install, Path.GetFileName(FakeGameProcess.ExecutablePath)), Path.Combine(install, exeFile), overwrite: true);
    }

    // Vanilla is the game as installed; Modded adds two mod files, patches a game file and has a second save. The folders end on Modded.
    // Capturing two profiles costs dozens of git processes, so the state is built once (see TwoProfileTemplates) and each call gets a copy of
    // it, reopened as the app is after a restart. The game's arguments and process name are then set for this test.
    public static async Task<(LaunchRig Rig, Profile Vanilla, Profile Modded)> WithTwoProfilesAsync(
        Func<BackendOptions, BackendOptions>? configure = null,
        IReadOnlyList<string>? gameArguments = null,
        bool followProcessName = false,
        ISteamClient? steam = null,
        SteamWaitOptions? steamWait = null)
    {
        var slot = await TwoProfileTemplates.RentAsync();
        BackendWorkspace? workspace = null;
        try
        {
            workspace = BackendWorkspace.CopyOf(slot.Workspace, WithOrchestrator(configure, null, null, steam, steamWait));
            var game = Assert.Single((await workspace.Games.ListAsync(workspace.Token)).Value!);
            var profiles = (await workspace.Profiles.ListAsync(game.Id, workspace.Token)).Value!;
            var rig = new LaunchRig(workspace, game, slot.ExeName, slot);
            if (gameArguments is not null || followProcessName)
            {
                var launch = game.Launch!;
                await rig.EditGameAsync(current => current with
                {
                    Launch = launch with
                    {
                        Args = [.. launch.Args, .. gameArguments ?? []],
                        ProcessNameForExitDetection = followProcessName ? rig.ExeName : null,
                    },
                });
            }

            return (rig, profiles.Single(profile => profile.Name == "Vanilla"), profiles.Single(profile => profile.Name == "Modded"));
        }
        catch
        {
            workspace?.Dispose();
            TwoProfileTemplates.Return(slot);
            throw;
        }
    }

    public async Task<(Profile Vanilla, Profile Modded)> BuildTwoProfilesAsync()
    {
        var vanilla = await ProfileAsync("Vanilla");
        WriteInstall("mods/a.txt", "mod a");
        WriteInstall("mods/b.txt", "mod b");
        WriteInstall("data/base.pak", "a longer, patched base content");
        WriteSave("slot2.sav", "second save");
        return (vanilla, await ProfileAsync("Modded"));
    }

    // Saves a profile from what the folders hold now.
    public async Task<Profile> ProfileAsync(string name) => (await Workspace.CreateProfileAsync(Game, name)).Profile;

    // Gives a game that had no saves folder one holding a save, as a player does later through Edit game; returns the folder.
    public async Task<string> AddSavesFolderAsync(string saveFile = "slot1.sav", string content = "the player's own save")
    {
        var saves = Path.Combine(Workspace.GamesRoot, "later-saves");
        BackendWorkspace.WriteFile(Path.Combine(saves, saveFile), content);
        var updated = await Workspace.Games.UpdateAsync(Game with { External = new ExternalLocation(saves) });
        Assert.True(updated.Ok, updated.Error?.Detail);
        Game = updated.Value;
        return saves;
    }

    // Edits the game's saved definition, as Edit game does, and keeps the rig's copy current.
    public async Task EditGameAsync(Func<GameDefinition, GameDefinition> change)
    {
        var updated = await Workspace.Games.UpdateAsync(change(Game));
        Assert.True(updated.Ok, updated.Error?.Detail);
        Game = updated.Value;
    }

    public void WriteInstall(string relativePath, string content) => BackendWorkspace.WriteFile(Path.Combine(InstallPath, relativePath), content);

    public void WriteSave(string relativePath, string content) => BackendWorkspace.WriteFile(Path.Combine(SavesPath, relativePath), content);

    public string ReadInstall(string relativePath) => File.ReadAllText(Path.Combine(InstallPath, relativePath));

    public string ReadSave(string relativePath) => File.ReadAllText(Path.Combine(SavesPath, relativePath));

    public bool InstallHas(string relativePath) => File.Exists(Path.Combine(InstallPath, relativePath));

    public bool SavesHave(string relativePath) => File.Exists(Path.Combine(SavesPath, relativePath));

    // A capture of both folders into the profile the folders are on, waited for.
    public async Task<JobSnapshot> CaptureAsync(Profile profile, string pointName, params RepoRole[] folders)
    {
        var started = await Profiles.CaptureAsync(Game.Id, profile.Id, pointName, folders.Length == 0 ? null : folders);
        Assert.True(started.Ok, started.Error?.Detail);
        return await WaitForJobAsync(started.Value.JobId);
    }

    // The job's last snapshot; a job that does not end in time fails the test and says where it stood.
    public async Task<JobSnapshot> WaitForJobAsync(Guid jobId)
    {
        try
        {
            return await Workspace.WaitForJobAsync(jobId, _jobPatience);
        }
        catch (OperationCanceledException) when (!Token.IsCancellationRequested)
        {
            var job = Jobs.Get(jobId);
            var lines = string.Join(" | ", job?.RecentLines.TakeLast(5) ?? []);
            throw new TimeoutException($"The job never ended: {job?.Status}, progress {job?.Progress}, last lines [{lines}]. The session went through: {string.Join(", ", Phases())}.");
        }
    }

    public async Task<LaunchPreview> PreviewAsync(LaunchTarget target)
    {
        var preview = await Launch.GetPreviewAsync(Game.Id, target, Token);
        Assert.True(preview.Ok, preview.Error?.Detail);
        return preview.Value;
    }

    // Starts the launch and waits for its job to end.
    public async Task<JobSnapshot> StartAsync(LaunchTarget target)
    {
        var started = await Launch.StartAsync(Game.Id, target);
        Assert.True(started.Ok, started.Error?.Detail);
        return await WaitForJobAsync(started.Value.JobId);
    }

    // Switches the folders without starting the game and waits for the job to end.
    public async Task<JobSnapshot> SwitchAsync(LaunchTarget target)
    {
        var started = await Launch.SwitchAsync(Game.Id, target);
        Assert.True(started.Ok, started.Error?.Detail);
        return await WaitForJobAsync(started.Value.JobId);
    }

    public async Task<LaunchSessionInfo> WaitForAsync(Func<LaunchSessionInfo, bool> condition, string described)
    {
        var until = DateTime.UtcNow + _patience;
        while (true)
        {
            if (Launch.GetSession(Game.Id) is { } now && condition(now))
            {
                return now;
            }

            if (DateTime.UtcNow > until)
            {
                var seen = string.Join(", ", Events.Select(e => e.State.GetType().Name));
                throw new TimeoutException($"The session never reached: {described}. It went through: {seen}.");
            }

            await Task.Delay(20, Token);
        }
    }

    // Waits until the event stream itself has delivered an event, which can trail the session the Api already shows.
    public async Task WaitForEventAsync(Func<LaunchSessionInfo, bool> condition, string described)
    {
        var until = DateTime.UtcNow + _patience;
        while (!Events.Any(condition))
        {
            if (DateTime.UtcNow > until)
            {
                throw new TimeoutException($"The event stream never delivered: {described}.");
            }

            await Task.Delay(20, Token);
        }
    }

    public Task<LaunchSessionInfo> WaitForAsync<TState>()
        where TState : SessionState =>
        WaitForAsync(session => session.State is TState, typeof(TState).Name);

    // The phases the session has been through, oldest first, with a phase that was reported several times named once.
    public IReadOnlyList<string> Phases()
    {
        var phases = new List<string>();
        foreach (var name in Events.Select(e => e.State.GetType().Name))
        {
            if (phases.Count == 0 || phases[^1] != name)
            {
                phases.Add(name);
            }
        }

        return phases;
    }

    // The stand-in game process ids, found by the name only this rig's copy has.
    public IReadOnlyList<int> GameProcessIds() => [.. Process.GetProcessesByName(ExeName).Select(process => process.Id)];

    public async Task WaitForGameAsync()
    {
        Assert.True(await FakeGameProcess.WaitUntilAsync(() => File.Exists(ReadyFile)), "the game never reported that it was running");
    }

    // Starts one more copy of the stand-in game by hand, as a store would start it again after the first one quit. Disposing kills it with the rest.
    public void StartAnotherCopyOfTheGame()
    {
        var file = ExeName + (OperatingSystem.IsWindows() ? ".exe" : string.Empty);
        var info = new ProcessStartInfo(Path.Combine(InstallPath, file)) { WorkingDirectory = InstallPath, UseShellExecute = false, CreateNoWindow = true };
        Process.Start(info)?.Dispose();
    }

    public void KillGame()
    {
        foreach (var id in GameProcessIds())
        {
            FakeGameProcess.Kill(id);
        }
    }

    // The folder's own view of itself, read from its git directory.
    public Task<RepoStatus> StatusAsync(RepoRole role) =>
        Workspace.Backend.Services.Storage.ReadAsync(async storage =>
            await storage.Engine.GetStatusAsync(Repo(storage, role), Token));

    public Task<IReadOnlyList<CommitInfo>> CommitsAsync(RepoRole role, Profile profile) =>
        Workspace.Backend.Services.Storage.ReadAsync(storage =>
            storage.Engine.ListCommitsAsync(Repo(storage, role), profile.BranchName, null, Token));

    // Holds the switch of the install folder at its first free-space question once the job has said it is switching that folder. The
    // checks that come before the switch ask too, so counting the questions would tie a test to how many checks a launch makes.
    public SwitchHold HoldInstallSwitch(TimeSpan patience)
    {
        var hold = new SwitchHold(patience, Workspace.Disk);
        Workspace.Disk.FreePerPath = path => path.StartsWith(InstallPath, StringComparison.OrdinalIgnoreCase) && InstallSwitchBegun() ? hold.Arrive() : null;
        return hold;
    }

    private bool InstallSwitchBegun() =>
        Jobs.List().Any(job => job.GameId == Game.Id
            && job.RecentLines.OfType<JobLine.Step>().Any(step => step.MessageId == JobSteps.LaunchSwitchFolder && Equals(step.Args["folder"], "install")));

    public Task<T> WithEngineAsync<T>(Func<GitEngine, RepoRef, Task<T>> work, RepoRole role) =>
        Workspace.Backend.Services.Storage.ReadAsync(storage => work(storage.Engine, Repo(storage, role)));

    private RepoRef Repo(StorageSnapshot storage, RepoRole role) =>
        GameFolders.Repos(Workspace.Backend.Services, storage, Game).Single(repo => repo.Role == role);

    public IReadOnlyDictionary<string, string> SnapshotOf(RepoRole role) =>
        BackendWorkspace.Snapshot(role == RepoRole.Install ? InstallPath : SavesPath);

    private void OnSessionChanged(object? sender, LaunchSessionInfo session)
    {
        lock (_gate)
        {
            _events.Add(session);
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        Launch.SessionChanged -= OnSessionChanged;
        KillGame();

        // A killed program lets go of its files a moment later; the folders cannot be removed until it has.
        SpinWait.SpinUntil(() => GameProcessIds().Count == 0, TimeSpan.FromSeconds(10));
        Workspace.Dispose();

        // Only now may another test use the program's name.
        if (_slot is not null)
        {
            TwoProfileTemplates.Return(_slot);
        }
    }
}

// A switch held where it asks for room, so a test can stop it before it writes a file. Disposing lets it go and puts the disk figures back.
internal sealed class SwitchHold : IDisposable
{
    private readonly TimeSpan _patience;
    private readonly FakeDiskSpace _disk;
    private readonly ManualResetEventSlim _reached = new();
    private readonly ManualResetEventSlim _release = new();
    private int _held;

    public SwitchHold(TimeSpan patience, FakeDiskSpace disk)
    {
        _patience = patience;
        _disk = disk;
    }

    // Called from the switch's own thread: the first question is held, any later one passes.
    public long? Arrive()
    {
        if (!_release.IsSet && Interlocked.Exchange(ref _held, 1) == 0)
        {
            _reached.Set();
            _release.Wait(_patience);
        }

        return null;
    }

    // True when the switch has reached the hold; a switch that never does fails the test within the patience.
    public bool WaitUntilReached(CancellationToken cancellationToken) => _reached.Wait(_patience, cancellationToken);

    public void Release() => _release.Set();

    public void Dispose()
    {
        _release.Set();
        _disk.FreePerPath = null;
        _reached.Dispose();
        _release.Dispose();
    }
}
