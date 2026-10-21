using System.Diagnostics;
using Savepoint.Core.Api;
using Savepoint.Core.Backend;
using Savepoint.Core.Catalog;
using Savepoint.Core.Git;
using Savepoint.Core.Launch;
using Savepoint.Core.Launch.Orchestration;
using Savepoint.Core.Services;
using Savepoint.Core.Tests.Backend;
using Savepoint.Core.Tests.Services.Exe;

namespace Savepoint.Core.Tests.Launch.Orchestration;

// A whole backend with the real orchestrator, over a game whose install folder holds a private copy of the stand-in game (under a
// process name no other test shares) and whose saves folder is separate. Disposing kills whatever is still running under that name.
internal sealed class LaunchRig : IDisposable
{
    private static readonly TimeSpan _patience = TimeSpan.FromSeconds(60);

    private readonly object _gate = new();
    private readonly List<LaunchSessionInfo> _events = [];

    private LaunchRig(BackendWorkspace workspace, GameDefinition game, string exeName)
    {
        Workspace = workspace;
        Game = game;
        ExeName = exeName;
        Launch.SessionChanged += OnSessionChanged;
    }

    public BackendWorkspace Workspace { get; }

    public GameDefinition Game { get; }

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
        string? processName = null,
        bool withSaves = true,
        GameSessionOptions? session = null)
    {
        var exeName = "fg" + Guid.NewGuid().ToString("N")[..6];
        var exeFile = exeName + (OperatingSystem.IsWindows() ? ".exe" : string.Empty);
        var rigSession = session ?? new GameSessionOptions { PollInterval = TimeSpan.FromMilliseconds(100), Grace = TimeSpan.FromSeconds(3) };
        var workspace = new BackendWorkspace(options =>
        {
            var launching = options with { Launch = LaunchOrchestrator.Factory(new LaunchOrchestratorOptions { Session = rigSession }) };
            return configure is null ? launching : configure(launching);
        });
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
            var game = definition with { Launch = new LaunchConfig(LaunchMethod.Exe, exePath ?? exeFile, null, arguments, processName) };
            return new LaunchRig(workspace, await workspace.AddGameAsync(game), exeName);
        }
        catch
        {
            workspace.Dispose();
            throw;
        }
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

    // Saves a profile from what the folders hold now.
    public async Task<Profile> ProfileAsync(string name) => (await Workspace.CreateProfileAsync(Game, name)).Profile;

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
        return await Workspace.WaitForJobAsync(started.Value.JobId);
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
        return await Workspace.WaitForJobAsync(started.Value.JobId);
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

    public Task<LaunchSessionInfo> WaitForAsync<TState>()
        where TState : SessionState =>
        WaitForAsync(session => session.State is TState, typeof(TState).Name);

    // The stand-in game process ids, found by the name only this rig's copy has.
    public IReadOnlyList<int> GameProcessIds() => [.. Process.GetProcessesByName(ExeName).Select(process => process.Id)];

    public async Task WaitForGameAsync()
    {
        Assert.True(await FakeGameProcess.WaitUntilAsync(() => File.Exists(ReadyFile)), "the game never reported that it was running");
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
        Launch.SessionChanged -= OnSessionChanged;
        KillGame();

        // A killed program lets go of its files a moment later; the folders cannot be removed until it has.
        SpinWait.SpinUntil(() => GameProcessIds().Count == 0, TimeSpan.FromSeconds(10));
        Workspace.Dispose();
    }
}
