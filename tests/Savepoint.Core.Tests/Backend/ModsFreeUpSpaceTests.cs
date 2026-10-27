using Savepoint.Core.Api;
using Savepoint.Core.Backend;
using Savepoint.Core.Catalog;
using Savepoint.Core.Git;
using Savepoint.Core.Services.Storage;

namespace Savepoint.Core.Tests.Backend;

// Recently removed snapshots and Free up space cover the mods store like the other two: a continue from the first snapshot takes the
// later ones off the profile in every folder, and the free-up note names the records of all three before the first is deleted.
public class ModsFreeUpSpaceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly RepoRole[] _allFolders = [RepoRole.Install, RepoRole.Mods, RepoRole.External];

    [Fact(Timeout = 240_000)]
    public async Task A_snapshot_only_the_mods_folder_recorded_is_listed_as_removed_with_the_mods_it_held()
    {
        using var world = await World.CreateAsync();

        var listed = await world.ListAsync();

        var modsOnly = listed.Single(snapshot => snapshot.Id == world.ModsOnly);
        Assert.Equal(HistoryPointKind.VersionAndMods, modsOnly.Kind);
        Assert.Equal("Added a mod", modsOnly.Name);
    }

    [Fact(Timeout = 240_000)]
    public async Task Bringing_back_a_snapshot_only_the_mods_folder_recorded_starts_a_profile_with_that_mods_point_and_the_other_folders_as_they_were()
    {
        using var world = await World.CreateAsync();

        var brought = await world.Workspace.Profiles.BringBackAsync(world.Game.Id, world.ModsOnly, "Back with mod c");

        Assert.True(brought.Ok, brought.Error?.Detail);
        var mods = await world.Workspace.CommitsAsync(world.Game, RepoRole.Mods, brought.Value);
        Assert.Equal(world.ModsOnly.Value, mods[0].CaptureId);
        Assert.Contains("modC/info.xml", await world.Workspace.FilesAsync(world.Game, RepoRole.Mods, brought.Value));
        Assert.NotEmpty(await world.Workspace.CommitsAsync(world.Game, RepoRole.Install, brought.Value));
        Assert.Single(await world.ListAsync(), snapshot => snapshot.Id != world.ModsOnly);
    }

    [Fact(Timeout = 240_000)]
    public async Task The_preview_counts_the_snapshots_and_names_the_records_of_every_store()
    {
        using var world = await World.CreateAsync();

        var preview = (await world.Workspace.Games.GetFreeUpSpacePreviewAsync(world.Game.Id, Ct)).Value!;

        Assert.Equal(2, preview.RecentlyRemovedSnapshots);
        Assert.Contains(preview.Token.Records, record => record.Folder == RepoRole.Mods);
        Assert.Contains(preview.Token.Records, record => record.Folder == RepoRole.Install);
    }

    [Fact(Timeout = 240_000)]
    public async Task Freeing_up_space_clears_the_removed_records_in_the_mods_store_too()
    {
        using var world = await World.CreateAsync();
        var preview = (await world.Workspace.Games.GetFreeUpSpacePreviewAsync(world.Game.Id, Ct)).Value!;
        Assert.NotEmpty(await world.RecordedCommitsAsync(RepoRole.Mods));

        var started = await world.Workspace.Games.OptimizeStorageAsync(world.Game.Id, preview.Token);
        var job = await world.Workspace.WaitForJobAsync(started.Value!.JobId);

        Assert.Equal(JobStatus.Succeeded, job.Status);
        Assert.Empty(await world.RecordedCommitsAsync(RepoRole.Mods));
        Assert.Empty(await world.ListAsync());
        var gameDirectory = (await world.Workspace.Backend.Services.Storage.GetAsync(Ct)).GameDirectory(world.Game.Id);
        Assert.False(File.Exists(FreeUpIntent.PathIn(gameDirectory)));
    }

    [Theory(Timeout = 240_000)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task A_stop_at_any_step_of_a_three_store_clear_is_finished_by_the_next_start(int step)
    {
        using var world = await World.CreateAsync();
        var preview = (await world.Workspace.Games.GetFreeUpSpacePreviewAsync(world.Game.Id, Ct)).Value!;
        var gameDirectory = (await world.Workspace.Backend.Services.Storage.GetAsync(Ct)).GameDirectory(world.Game.Id);
        await world.StopAfterAsync(preview.Token, step);

        Assert.Empty(await world.ListAsync());
        world.Workspace.Restart();
        var status = await world.Workspace.AppStatus.CheckAsync(Ct);

        Assert.True(status.Ok, status.Error?.Detail);
        Assert.False(File.Exists(FreeUpIntent.PathIn(gameDirectory)));
        foreach (var role in _allFolders)
        {
            Assert.Empty(await world.RecordedCommitsAsync(role));
        }
    }

    [Fact(Timeout = 240_000)]
    public async Task The_note_names_the_mods_records_under_their_own_key()
    {
        using var world = await World.CreateAsync();
        var preview = (await world.Workspace.Games.GetFreeUpSpacePreviewAsync(world.Game.Id, Ct)).Value!;
        var gameDirectory = (await world.Workspace.Backend.Services.Storage.GetAsync(Ct)).GameDirectory(world.Game.Id);
        await world.StopAfterAsync(preview.Token, 1);

        var note = FreeUpIntent.Read(gameDirectory)!;
        using var document = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(FreeUpIntent.PathIn(gameDirectory), Ct));

        Assert.NotEmpty(note[RepoRole.Mods]);
        Assert.True(document.RootElement.TryGetProperty("mods", out _));
    }

    [Fact(Timeout = 240_000)]
    public async Task A_note_with_only_the_install_and_saves_keys_still_finishes()
    {
        using var world = await World.CreateAsync();
        var preview = (await world.Workspace.Games.GetFreeUpSpacePreviewAsync(world.Game.Id, Ct)).Value!;
        var storage = await world.Workspace.Backend.Services.Storage.GetAsync(Ct);
        var gameDirectory = storage.GameDirectory(world.Game.Id);
        var plans = new Dictionary<RepoRole, IReadOnlyList<ClearedRef>>();
        foreach (var role in new[] { RepoRole.Install, RepoRole.External })
        {
            var names = preview.Token.Records.Where(record => record.Folder == role).Select(record => record.RefName).ToList();
            plans[role] = await storage.Engine.FindClearableAsync(world.RepoOf(storage, role), names, Ct);
        }

        FreeUpIntent.Write(gameDirectory, plans);
        world.Workspace.Restart();
        var status = await world.Workspace.AppStatus.CheckAsync(Ct);

        Assert.True(status.Ok, status.Error?.Detail);
        Assert.False(File.Exists(FreeUpIntent.PathIn(gameDirectory)));
        Assert.Empty(await world.RecordedCommitsAsync(RepoRole.Install));
    }

    // One game with a mods folder and one profile. "Vanilla" holds the first snapshot in every folder, then "Added a mod" in the mods
    // folder alone, then "More of everything" in all three; a continue from the first takes the other two off the profile.
    private sealed class World : IDisposable
    {
        private World(BackendWorkspace workspace, GameDefinition game, Profile profile, HistoryPointId modsOnly)
        {
            Workspace = workspace;
            Game = game;
            Profile = profile;
            ModsOnly = modsOnly;
        }

        public BackendWorkspace Workspace { get; }

        public GameDefinition Game { get; }

        public Profile Profile { get; }

        public HistoryPointId ModsOnly { get; }

        public static async Task<World> CreateAsync()
        {
            var workspace = new BackendWorkspace();
            try
            {
                var game = await workspace.AddGameAsync(workspace.NewGameWithMods("Alpha") with { ModPathHints = ["data"] });
                var (profile, created) = await workspace.CreateProfileAsync(game, "Vanilla");
                var first = Assert.IsType<CaptureResult>(created.Result).PointId!.Value;
                BackendWorkspace.WriteFile(Path.Combine(game.Mods!.Path, "modC", "info.xml"), "mod c");
                var modsOnly = await CaptureAsync(workspace, game, profile, "Added a mod", RepoRole.Mods);
                BackendWorkspace.WriteFile(Path.Combine(game.Install.Path, "data", "patch.pak"), "a patch");
                BackendWorkspace.WriteFile(Path.Combine(game.Mods.Path, "modD", "info.xml"), "mod d");
                BackendWorkspace.WriteFile(Path.Combine(game.External!.Path, "slot2.sav"), "second save");
                await CaptureAsync(workspace, game, profile, "More of everything", RepoRole.Install, RepoRole.Mods, RepoRole.External);

                var world = new World(workspace, game, profile, modsOnly);
                await world.ContinueFromAsync(first);
                return world;
            }
            catch
            {
                workspace.Dispose();
                throw;
            }
        }

        private static async Task<HistoryPointId> CaptureAsync(BackendWorkspace workspace, GameDefinition game, Profile profile, string name, params RepoRole[] folders)
        {
            var started = await workspace.Profiles.CaptureAsync(game.Id, profile.Id, name, folders);
            Assert.True(started.Ok, started.Error?.Detail);
            var job = await workspace.WaitForJobAsync(started.Value.JobId);
            return Assert.IsType<CaptureResult>(job.Result).PointId!.Value;
        }

        public RepoRef RepoOf(StorageSnapshot storage, RepoRole role) =>
            new(storage.GitDirectory(Game.Id, role), role switch { RepoRole.Install => Game.Install.Path, RepoRole.Mods => Game.Mods!.Path, _ => Game.External!.Path }, Game.Id, role);

        // Plays on from the first snapshot and continues the profile from there, as the after-play "continue" does.
        private async Task ContinueFromAsync(HistoryPointId first)
        {
            var storage = await Workspace.Backend.Services.Storage.GetAsync(Ct);
            var captureId = Guid.NewGuid().ToString("D");
            var at = DateTimeOffset.UtcNow;
            foreach (var role in _allFolders)
            {
                var repo = RepoOf(storage, role);
                var lineage = await storage.Engine.ListCommitsAsync(repo, Profile.BranchName, ct: Ct);
                var start = lineage.Single(commit => commit.CaptureId == first.Value);
                await storage.Engine.CheckoutCommitAsync(repo, start.Id, ct: Ct);
                var request = new CaptureRequest(Profile.BranchName, "Continued", captureId) { Mode = CaptureMode.MoveForward, ExpectedStartCommit = start.Id, CapturedAt = at };
                await storage.Engine.CaptureAsync(repo, request, ct: Ct);
            }
        }

        public async Task<IReadOnlyList<RemovedSnapshot>> ListAsync()
        {
            var read = await Workspace.History.GetRecentlyRemovedAsync(Game.Id, Ct);
            Assert.True(read.Ok, read.Error?.Detail);
            return read.Value.Snapshots;
        }

        // The commits a record keeps that no branch reaches: what the folder lists as removed.
        public async Task<IReadOnlyList<string>> RecordedCommitsAsync(RepoRole role)
        {
            var storage = await Workspace.Backend.Services.Storage.GetAsync(Ct);
            return [.. (await storage.Engine.ListRemovedAsync(RepoOf(storage, role), Ct)).Where(removed => !removed.OnBranch).Select(removed => removed.Commit.Id)];
        }

        // The folders as a run leaves them when it stops at that step: the note first, then the stores in turn.
        public async Task StopAfterAsync(FreeUpSpaceToken token, int step)
        {
            var storage = await Workspace.Backend.Services.Storage.GetAsync(Ct);
            var plans = new Dictionary<RepoRole, IReadOnlyList<ClearedRef>>();
            foreach (var role in _allFolders)
            {
                var names = token.Records.Where(record => record.Folder == role).Select(record => record.RefName).ToList();
                plans[role] = await storage.Engine.FindClearableAsync(RepoOf(storage, role), names, Ct);
            }

            FreeUpIntent.Write(storage.GameDirectory(Game.Id), plans);
            for (var done = 0; done < step - 1 && done < _allFolders.Length; done++)
            {
                await storage.Engine.ClearExactAsync(RepoOf(storage, _allFolders[done]), plans[_allFolders[done]], Ct);
            }
        }

        public void Dispose() => Workspace.Dispose();
    }
}
