using Savepoint.Core.Api;
using Savepoint.Core.Backend;
using Savepoint.Core.Catalog;
using Savepoint.Core.Git;

namespace Savepoint.Core.Tests.Backend;

// A game with one profile and three snapshots, built through the Api, and the two ways a save takes snapshots off a profile, done through
// the engine the way a play session's after-play choices do them. Nothing here starts a game.
internal sealed class RemovedWorld : IDisposable
{
    private RemovedWorld(BackendWorkspace workspace, GameDefinition game, Profile profile, HistoryPointId first, HistoryPointId installOnly, HistoryPointId savesOnly)
    {
        Workspace = workspace;
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
    public static async Task<RemovedWorld> CreateAsync(Func<BackendOptions, BackendOptions>? configure = null)
    {
        var workspace = new BackendWorkspace(configure);
        try
        {
            var game = await workspace.AddGameAsync(workspace.NewGame(modHints: ["mods"]));
            var (profile, created) = await workspace.CreateProfileAsync(game, "Vanilla");
            var first = Assert.IsType<CaptureResult>(created.Result).PointId!.Value;

            BackendWorkspace.WriteFile(Path.Combine(game.Install.Path, "mods", "a.esp"), "a mod");
            var installOnly = await CaptureAsync(workspace, game, profile, "Added a mod", RepoRole.Install);
            BackendWorkspace.WriteFile(Path.Combine(game.External!.Path, "slot2.sav"), "second save");
            var savesOnly = await CaptureAsync(workspace, game, profile, "Second save", RepoRole.External);
            return new RemovedWorld(workspace, game, profile, first, installOnly, savesOnly);
        }
        catch
        {
            workspace.Dispose();
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
    public async Task ContinueFromFirstAsync()
    {
        var storage = await Workspace.Backend.Services.Storage.GetAsync(Token);
        var at = DateTimeOffset.UtcNow;
        var captureId = Guid.NewGuid().ToString("D");
        foreach (var role in new[] { RepoRole.Install, RepoRole.External })
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

    public async Task<IReadOnlyList<string>> RecordedCommitsAsync(RepoRole role)
    {
        var storage = await Workspace.Backend.Services.Storage.GetAsync(Token);
        return [.. (await storage.Engine.ListRemovedAsync(RepoOf(storage, role), Token)).Select(removed => removed.Commit.Id)];
    }

    public void Dispose() => Workspace.Dispose();
}
