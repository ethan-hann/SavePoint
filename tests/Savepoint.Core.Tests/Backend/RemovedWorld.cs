using Savepoint.Core.Api;
using Savepoint.Core.Backend;
using Savepoint.Core.Catalog;
using Savepoint.Core.Git;

namespace Savepoint.Core.Tests.Backend;

// A game with one profile and three snapshots, built through the Api, and the two ways a save takes snapshots off a profile, done through
// the engine the way a play session's after-play choices do them. Nothing here starts a game.
internal sealed class RemovedWorld : IDisposable
{
    private readonly bool _ownsWorkspace;

    private RemovedWorld(BackendWorkspace workspace, bool ownsWorkspace, GameDefinition game, Profile profile, HistoryPointId first, HistoryPointId installOnly, HistoryPointId savesOnly)
    {
        Workspace = workspace;
        _ownsWorkspace = ownsWorkspace;
        Game = game;
        Profile = profile;
        First = first;
        InstallOnly = installOnly;
        SavesOnly = savesOnly;
    }

    public BackendWorkspace Workspace { get; }

    public GameDefinition Game { get; }

    public Profile Profile { get; }

    // Both folders.
    public HistoryPointId First { get; }

    // The install folder only: a mod file added.
    public HistoryPointId InstallOnly { get; }

    // The saves folder only: a second save.
    public HistoryPointId SavesOnly { get; }

    private CancellationToken Token => Workspace.Token;

    // "Vanilla" holds three snapshots, oldest first: both folders, then the install folder with a mod file, then the saves folder with a second save.
    // Another game of the same shape can be built in a workspace that another world already owns.
    public static async Task<RemovedWorld> CreateAsync(Func<BackendOptions, BackendOptions>? configure = null, BackendWorkspace? shared = null, string name = "Alpha")
    {
        var workspace = shared ?? new BackendWorkspace(configure);
        try
        {
            var game = await workspace.AddGameAsync(workspace.NewGame(name, modHints: ["mods"]));
            var (profile, created) = await workspace.CreateProfileAsync(game, "Vanilla");
            var first = Assert.IsType<CaptureResult>(created.Result).PointId!.Value;

            BackendWorkspace.WriteFile(Path.Combine(game.Install.Path, "mods", "a.esp"), "a mod");
            var installOnly = await CaptureAsync(workspace, game, profile, "Added a mod", RepoRole.Install);
            BackendWorkspace.WriteFile(Path.Combine(game.External!.Path, "slot2.sav"), "second save");
            var savesOnly = await CaptureAsync(workspace, game, profile, "Second save", RepoRole.External);
            return new RemovedWorld(workspace, shared is null, game, profile, first, installOnly, savesOnly);
        }
        catch
        {
            if (shared is null)
            {
                workspace.Dispose();
            }

            throw;
        }
    }

    // A both-folder snapshot besides the two single-folder ones, all three taken off the profile by a continue from the first, so a clear
    // that reaches one folder and not the other shows as a changed kind. The hooks see every git call the engine makes.
    public static async Task<RemovedWorld> AfterContinueAsync(GitHooks hooks)
    {
        var world = await CreateAsync(options => options with { Engine = new GitEngineOptions { InvocationObserver = hooks.Observe } });
        try
        {
            BackendWorkspace.WriteFile(Path.Combine(world.Game.Install.Path, "mods", "b.esp"), "another mod");
            BackendWorkspace.WriteFile(Path.Combine(world.Game.External!.Path, "slot3.sav"), "third save");
            await CaptureAsync(world.Workspace, world.Game, world.Profile, "Both again", RepoRole.Install, RepoRole.External);
            await world.ContinueFromFirstAsync();
            Assert.Equal(3, (await world.ListAsync()).Count);
            return world;
        }
        catch
        {
            world.Dispose();
            throw;
        }
    }

    public static async Task<HistoryPointId> CaptureAsync(BackendWorkspace workspace, GameDefinition game, Profile profile, string name, params RepoRole[] folders)
    {
        var started = await workspace.Profiles.CaptureAsync(game.Id, profile.Id, name, folders);
        Assert.True(started.Ok, started.Error?.Detail);
        var job = await workspace.WaitForJobAsync(started.Value.JobId);
        return Assert.IsType<CaptureResult>(job.Result).PointId!.Value;
    }

    public RepoRef RepoOf(StorageSnapshot storage, RepoRole role) =>
        new(storage.GitDirectory(Game.Id, role), role == RepoRole.Install ? Game.Install.Path : Game.External!.Path, Game.Id, role);

    // Plays on from the first snapshot and continues the profile from there: each folder goes back to its commit for that snapshot, and
    // the profile's branch moves to it, which takes the other two snapshots off the profile.
    public Task ContinueFromFirstAsync() => ContinueAsync([RepoRole.Install, RepoRole.External]);

    // The same, for the saves folder alone: the install folder is left on the profile as it was.
    public Task ContinueSavesFromFirstAsync() => ContinueAsync([RepoRole.External]);

    private async Task ContinueAsync(IReadOnlyList<RepoRole> folders)
    {
        var storage = await Workspace.Backend.Services.Storage.GetAsync(Token);
        var at = DateTimeOffset.UtcNow;
        var captureId = Guid.NewGuid().ToString("D");
        foreach (var role in folders)
        {
            var repo = RepoOf(storage, role);
            var lineage = await storage.Engine.ListCommitsAsync(repo, Profile.BranchName, ct: Token);
            var start = lineage.Single(commit => commit.CaptureId == First.Value);
            await storage.Engine.CheckoutCommitAsync(repo, start.Id, ct: Token);
            var request = new CaptureRequest(Profile.BranchName, "Continued", captureId) { Mode = CaptureMode.MoveForward, ExpectedStartCommit = start.Id, CapturedAt = at };
            await storage.Engine.CaptureAsync(repo, request, ct: Token);
        }
    }

    // Replaces the latest snapshot of each folder with what the folders hold now, as "Replace with this session" does.
    public async Task ReplaceLatestAsync(string name)
    {
        var storage = await Workspace.Backend.Services.Storage.GetAsync(Token);
        var at = DateTimeOffset.UtcNow;
        var captureId = Guid.NewGuid().ToString("D");
        foreach (var role in new[] { RepoRole.Install, RepoRole.External })
        {
            var request = new CaptureRequest(Profile.BranchName, name, captureId) { Mode = CaptureMode.Amend, CapturedAt = at };
            await storage.Engine.CaptureAsync(RepoOf(storage, role), request, ct: Token);
        }
    }

    public async Task<IReadOnlyList<RemovedSnapshot>> ListAsync()
    {
        var read = await Workspace.History.GetRecentlyRemovedAsync(Game.Id, Token);
        Assert.True(read.Ok, read.Error?.Detail);
        return read.Value.Snapshots;
    }

    public async Task<string?> BranchTipAsync(string branch, RepoRole role)
    {
        var storage = await Workspace.Backend.Services.Storage.GetAsync(Token);
        return await storage.Engine.GetBranchTipAsync(RepoOf(storage, role), branch, Token);
    }

    public async Task<IReadOnlyList<CommitInfo>> CommitsAsync(string branch, RepoRole role)
    {
        var storage = await Workspace.Backend.Services.Storage.GetAsync(Token);
        return await storage.Engine.ListCommitsAsync(RepoOf(storage, role), branch, ct: Token);
    }

    // The commits a record keeps that no branch reaches: what the folder lists as removed.
    public async Task<IReadOnlyList<string>> RecordedCommitsAsync(RepoRole role)
    {
        var storage = await Workspace.Backend.Services.Storage.GetAsync(Token);
        return [.. (await storage.Engine.ListRemovedAsync(RepoOf(storage, role), Token)).Where(removed => !removed.OnBranch).Select(removed => removed.Commit.Id)];
    }

    // True when the store still holds the commit, loose or packed.
    public async Task<bool> ObjectExistsAsync(RepoRole role, string commitId)
    {
        var storage = await Workspace.Backend.Services.Storage.GetAsync(Token);
        var repo = RepoOf(storage, role);
        var result = await Workspace.Backend.Services.Runner.RunAsync(
            new GitInvocation(["cat-file", "-e", commitId]) { GitDir = repo.GitDir, WorkTree = repo.WorkTree, ReadOnly = true, AllowedExitCodes = [0, 1, 128] },
            Token);
        return result.ExitCode == 0;
    }

    public void Dispose()
    {
        if (_ownsWorkspace)
        {
            Workspace.Dispose();
        }
    }
}
