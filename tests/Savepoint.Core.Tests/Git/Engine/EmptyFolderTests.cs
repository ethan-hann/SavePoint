using Savepoint.Core.Git;

namespace Savepoint.Core.Tests.Git.Engine;

// A snapshot keeps the empty folders its game folder had, because git does not: a game can ship one that its mod tools expect.
[Collection(EngineCollections.F)]
public sealed class EmptyFolderTests : IDisposable
{
    private const string _modFolder = "archive/pc/mod";

    private readonly EngineWorkspace _ws = new(options => options with { ResyncDelays = [TimeSpan.FromMilliseconds(20), TimeSpan.FromMilliseconds(20)] });

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _ws.Dispose();

    private bool Exists(RepoRef repo, string folder) => Directory.Exists(_ws.FileOf(repo, folder));

    // profile-a holds the game's files and an empty folder; profile-b is forked from it and fills the folder. The folder is left on profile-b.
    private async Task<RepoRef> VanillaAndModdedAsync(IReadOnlyList<string>? ignore = null, string emptyFolder = _modFolder)
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "game.exe", "exe");
        Directory.CreateDirectory(_ws.FileOf(repo, emptyFolder));
        await _ws.CaptureAsync(repo, "profile-a", ignore: ignore);
        await _ws.GitWriteAsync(repo, "checkout", "--quiet", "-b", "profile-b");
        _ws.Write(repo, emptyFolder + "/tweak.archive", "mod");
        await _ws.CaptureAsync(repo, "profile-b", ignore: ignore);
        return repo;
    }

    // One snapshot of the game files and the empty folder, with the folder on it.
    private async Task<RepoRef> OneSnapshotAsync()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "game.exe", "exe");
        Directory.CreateDirectory(_ws.FileOf(repo, _modFolder));
        await _ws.CaptureAsync(repo, "profile-a");
        return repo;
    }

    private async Task<string> TrailerAsync(RepoRef repo, string revision) =>
        await _ws.GitOutAsync(repo, "log", "-1", "--format=%(trailers:key=Savepoint-Empty-Folders,valueonly)", revision, "--");

    [Fact]
    public async Task A_snapshot_records_the_empty_folder_it_was_taken_with()
    {
        var repo = await VanillaAndModdedAsync();

        Assert.Equal("[\"archive/pc/mod\"]", await TrailerAsync(repo, "profile-a"));
        Assert.Equal(string.Empty, await TrailerAsync(repo, "profile-b"));
    }

    [Fact]
    public async Task Switching_back_to_the_profile_that_had_an_empty_folder_brings_the_folder_back()
    {
        var repo = await VanillaAndModdedAsync();

        await _ws.Engine.CheckoutBranchAsync(repo, "profile-a", ct: Ct);

        Assert.True(Exists(repo, _modFolder));
        Assert.Empty(Directory.GetFileSystemEntries(_ws.FileOf(repo, _modFolder)));
        Assert.True((await _ws.Engine.GetStatusAsync(repo, Ct)).IsClean);
    }

    [Fact]
    public async Task Going_back_to_an_older_snapshot_brings_back_its_empty_folder()
    {
        var repo = await VanillaAndModdedAsync();
        var older = (await _ws.ResolveAsync(repo, "profile-a"))!;

        await _ws.Engine.CheckoutCommitAsync(repo, older, ct: Ct);

        Assert.True(Exists(repo, _modFolder));
    }

    [Fact]
    public async Task A_forced_switch_brings_the_empty_folder_back_too()
    {
        var repo = await VanillaAndModdedAsync();
        _ws.Write(repo, _modFolder + "/tweak.archive", "edited since the snapshot");

        await _ws.Engine.ForceCheckoutBranchAsync(repo, "profile-a", Ct);

        Assert.True(Exists(repo, _modFolder));
        Assert.Empty(Directory.GetFileSystemEntries(_ws.FileOf(repo, _modFolder)));
    }

    [Fact]
    public async Task A_switch_that_was_stopped_and_is_finished_brings_the_empty_folder_back()
    {
        var repo = await VanillaAndModdedAsync();
        var from = await _ws.TipAsync(repo, "profile-b");
        var to = await _ws.TipAsync(repo, "profile-a");
        SwitchJournal.Write(repo.GitDir, from, to!, "profile-a");

        await _ws.Engine.CheckoutBranchAsync(repo, "profile-a", ct: Ct);

        Assert.True(Exists(repo, _modFolder));
        Assert.False(File.Exists(_ws.FileOf(repo, _modFolder + "/tweak.archive")));
    }

    [Fact]
    public async Task Empty_folders_inside_empty_folders_come_back_down_to_the_innermost_one()
    {
        var repo = await VanillaAndModdedAsync(emptyFolder: "a/b/c");

        await _ws.Engine.CheckoutBranchAsync(repo, "profile-a", ct: Ct);

        Assert.True(Exists(repo, "a/b/c"));
        Assert.Equal("[\"a/b/c\"]", await TrailerAsync(repo, "profile-a"));
    }

    [Fact]
    public async Task A_switch_does_not_leave_out_an_empty_folder_next_to_files()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "Data/keep.txt", "keep");
        Directory.CreateDirectory(_ws.FileOf(repo, "Data/Empty One"));
        Directory.CreateDirectory(_ws.FileOf(repo, "Data/café"));
        await _ws.CaptureAsync(repo, "profile-a");
        await _ws.GitWriteAsync(repo, "checkout", "--quiet", "-b", "profile-b");
        _ws.Write(repo, "Data/Empty One/x.txt", "x");
        _ws.Write(repo, "Data/café/y.txt", "y");
        await _ws.CaptureAsync(repo, "profile-b");

        await _ws.Engine.CheckoutBranchAsync(repo, "profile-a", ct: Ct);

        Assert.True(Exists(repo, "Data/Empty One"));
        Assert.True(Exists(repo, "Data/café"));
    }

    [Fact]
    public async Task A_folder_the_patterns_name_is_not_recorded_and_not_created()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "game.exe", "exe");
        Directory.CreateDirectory(_ws.FileOf(repo, "cache/shaders"));
        Directory.CreateDirectory(_ws.FileOf(repo, "kept"));
        await _ws.CaptureAsync(repo, "profile-a", ignore: ["cache/"]);
        await _ws.GitWriteAsync(repo, "checkout", "--quiet", "-b", "profile-b");
        _ws.Write(repo, "kept/x.txt", "x");
        await _ws.CaptureAsync(repo, "profile-b", ignore: ["cache/"]);
        Directory.Delete(_ws.FileOf(repo, "cache"), recursive: true);

        await _ws.Engine.CheckoutBranchAsync(repo, "profile-a", ct: Ct);

        Assert.Equal("[\"kept\"]", await TrailerAsync(repo, "profile-a"));
        Assert.True(Exists(repo, "kept"));
        Assert.False(Exists(repo, "cache"));
    }

    [Fact]
    public async Task A_pattern_added_after_the_snapshot_keeps_its_folder_from_being_created()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "game.exe", "exe");
        Directory.CreateDirectory(_ws.FileOf(repo, "cache/shaders"));
        await _ws.CaptureAsync(repo, "profile-a");
        await _ws.GitWriteAsync(repo, "checkout", "--quiet", "-b", "profile-b");
        Directory.Delete(_ws.FileOf(repo, "cache"), recursive: true);
        _ws.Write(repo, "other.txt", "other");
        await _ws.CaptureAsync(repo, "profile-b");

        await _ws.Engine.CheckoutBranchAsync(repo with { IgnorePatterns = ["cache/"] }, "profile-a", ct: Ct);

        Assert.Equal("[\"cache/shaders\"]", await TrailerAsync(repo, "profile-a"));
        Assert.False(Exists(repo, "cache"));
    }

    [Fact]
    public async Task An_ignore_file_inside_the_game_folder_does_not_unname_a_folder_the_patterns_name()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "game.exe", "exe");
        _ws.Write(repo, ".gitignore", "!cache/\n");
        Directory.CreateDirectory(_ws.FileOf(repo, "cache/shaders"));
        Directory.CreateDirectory(_ws.FileOf(repo, "kept"));

        await _ws.CaptureAsync(repo, "profile-a", ignore: ["cache/"]);

        Assert.Equal("[\"kept\"]", await TrailerAsync(repo, "profile-a"));
    }

    [Fact]
    public async Task A_folder_inside_a_folder_that_keeps_its_own_history_is_not_recorded()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "game.exe", "exe");
        Directory.CreateDirectory(_ws.FileOf(repo, "Mods/Nested/.git/objects"));
        Directory.CreateDirectory(_ws.FileOf(repo, "Mods/Nested/inner"));
        Directory.CreateDirectory(_ws.FileOf(repo, "Mods/Plain"));

        await _ws.CaptureAsync(repo, "profile-a");

        Assert.Equal("[\"Mods/Plain\"]", await TrailerAsync(repo, "profile-a"));
    }

    [Fact]
    public async Task A_snapshot_taken_before_empty_folders_were_recorded_behaves_as_before()
    {
        var repo = await VanillaAndModdedAsync();
        await _ws.GitWriteAsync(repo, "checkout", "--quiet", "profile-a");
        await _ws.GitWriteAsync(repo, "commit", "--amend", "--allow-empty", "--no-verify", "-m", "Taken long ago");
        Assert.Equal(string.Empty, await TrailerAsync(repo, "profile-a"));

        await _ws.Engine.CheckoutBranchAsync(repo, "profile-b", ct: Ct);
        await _ws.Engine.CheckoutBranchAsync(repo, "profile-a", ct: Ct);

        Assert.False(Exists(repo, _modFolder));
        Assert.True(File.Exists(_ws.FileOf(repo, "game.exe")));
    }

    [Fact]
    public async Task Discard_brings_back_an_empty_folder_of_the_snapshot_and_still_removes_new_ones()
    {
        var repo = await OneSnapshotAsync();
        Directory.Delete(_ws.FileOf(repo, _modFolder));
        Directory.CreateDirectory(_ws.FileOf(repo, "made/by/the/game"));
        _ws.Write(repo, "new.txt", "new");

        await _ws.Engine.DiscardChangesAsync(repo, Ct);

        Assert.True(Exists(repo, _modFolder));
        Assert.False(Exists(repo, "made"));
        Assert.False(File.Exists(_ws.FileOf(repo, "new.txt")));
    }

    [Fact]
    public async Task Clean_keeps_the_empty_folder_the_snapshot_had()
    {
        var repo = await OneSnapshotAsync();
        Directory.CreateDirectory(_ws.FileOf(repo, "made"));
        _ws.Write(repo, "new.txt", "new");

        await _ws.Engine.CleanAsync(repo, Ct);

        Assert.True(Exists(repo, _modFolder));
        Assert.False(Exists(repo, "made"));
    }

    [Fact]
    public async Task A_message_cannot_pass_itself_off_as_the_empty_folder_list()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "a.txt", "a");

        await Assert.ThrowsAsync<ArgumentException>(() => _ws.CaptureAsync(repo, "profile-a", "Savepoint-Empty-Folders: [\"x\"]"));
    }
}
