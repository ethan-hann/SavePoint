namespace Savepoint.Core.Tests.Git.Engine;

// What a save picks up from the folder, whatever way the engine asks git to list it: new, changed and removed files are recorded,
// a file the patterns name is left out, and a file git was told to skip is left as it is.
[Collection(EngineCollections.E)]
public sealed class CaptureListingTests : IDisposable
{
    private readonly EngineWorkspace _ws = new();

    public void Dispose() => _ws.Dispose();

    [Fact]
    public async Task A_save_records_new_changed_and_removed_files_whatever_their_names()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "keep.txt", "kept");
        _ws.Write(repo, "edit.txt", "before");
        _ws.Write(repo, "gone.txt", "gone");
        _ws.Write(repo, "Data/with space.txt", "spaced");
        _ws.Write(repo, "Données/é ü.txt", "accents");
        await _ws.CaptureAsync(repo);
        _ws.Write(repo, "edit.txt", "after, and longer");
        _ws.Delete(repo, "gone.txt");
        _ws.Delete(repo, "Data/with space.txt");
        _ws.Delete(repo, "Données/é ü.txt");
        _ws.Write(repo, "new file.txt", "new");
        _ws.Write(repo, "日本/語 ファイル.txt", "unicode");
        _ws.Write(repo, "T H X.txt", "tag-like");

        var outcome = await _ws.CaptureAsync(repo, "profile-1", "Second");

        Assert.True(outcome.Changed);
        var tree = await _ws.TreeAsync(repo, "profile-1");
        Assert.Equal(["T H X.txt", "edit.txt", "keep.txt", "new file.txt", "日本/語 ファイル.txt"], tree.Order(StringComparer.Ordinal));
        Assert.Equal("after, and longer", await _ws.GitOutAsync(repo, "show", "profile-1:edit.txt"));
    }

    [Fact]
    public async Task A_save_follows_a_file_that_became_a_folder_and_a_folder_that_became_a_file()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "was-file", "file");
        _ws.Write(repo, "was-folder/inner.txt", "inner");
        await _ws.CaptureAsync(repo);
        File.Delete(_ws.FileOf(repo, "was-file"));
        _ws.Write(repo, "was-file/now-inside.txt", "inside");
        Directory.Delete(_ws.FileOf(repo, "was-folder"), recursive: true);
        _ws.Write(repo, "was-folder", "now a file");

        await _ws.CaptureAsync(repo, "profile-1", "Second");

        var tree = await _ws.TreeAsync(repo, "profile-1");
        Assert.Equal(["was-file/now-inside.txt", "was-folder"], tree.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task A_save_leaves_out_a_new_file_the_patterns_name_and_keeps_the_rest()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "game.cfg", "cfg");
        await _ws.CaptureAsync(repo, ignore: ["logs/"]);
        _ws.Write(repo, "logs/run.log", "log");
        _ws.Write(repo, "game.cfg", "cfg changed");

        await _ws.CaptureAsync(repo, "profile-1", "Second", ignore: ["logs/"]);

        Assert.Equal(["game.cfg"], await _ws.TreeAsync(repo, "profile-1"));
        Assert.Equal("cfg changed", await _ws.GitOutAsync(repo, "show", "profile-1:game.cfg"));
    }

    [Fact]
    public async Task A_save_records_the_removal_of_a_recorded_file_even_when_git_was_told_to_skip_it()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "skipped.txt", "recorded");
        _ws.Write(repo, "other.txt", "other");
        await _ws.CaptureAsync(repo);
        await _ws.GitWriteAsync(repo, "update-index", "--skip-worktree", "skipped.txt");
        _ws.Delete(repo, "skipped.txt");
        _ws.Write(repo, "other.txt", "other changed");

        await _ws.CaptureAsync(repo, "profile-1", "Second");

        Assert.Equal(["other.txt"], await _ws.TreeAsync(repo, "profile-1"));
        Assert.Equal("other changed", await _ws.GitOutAsync(repo, "show", "profile-1:other.txt"));
    }

    [Fact]
    public async Task A_save_of_an_unchanged_folder_records_nothing()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "a.txt", "a");
        await _ws.CaptureAsync(repo);

        var outcome = await _ws.CaptureAsync(repo, "profile-1", "Again");

        Assert.False(outcome.Changed);
    }
}
