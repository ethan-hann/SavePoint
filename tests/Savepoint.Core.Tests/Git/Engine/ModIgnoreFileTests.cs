using Savepoint.Core.Git;

namespace Savepoint.Core.Tests.Git.Engine;

// A folder inside the game can carry its own ignore file (a mod cloned from a code host does). SavePoint has to store
// and restore exactly what is on disk, so only its own exclude list may leave a file out.
[Collection(EngineCollections.E)]
public sealed class ModIgnoreFileTests : IDisposable
{
    private static readonly byte[] _binary = [0x00, 0xFF, 0x0D, 0x0A, 0x41, 0x1A, 0x00, 0x80];

    private readonly EngineWorkspace _ws = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _ws.Dispose();

    // A mod folder whose ignore file names every .sav file in it.
    private async Task<RepoRef> RepoWithModAsync()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "mod/.gitignore", "*.sav\n");
        _ws.Write(repo, "mod/readme.txt", "readme");
        _ws.Write(repo, "mod/world.sav", _binary);
        return repo;
    }

    [Fact]
    public async Task A_file_the_ignore_file_of_a_mod_names_is_recorded()
    {
        var repo = await RepoWithModAsync();

        await _ws.CaptureAsync(repo, "profile-1");

        var tree = await _ws.TreeAsync(repo, "profile-1");
        Assert.Contains("mod/world.sav", tree);
        Assert.Contains("mod/.gitignore", tree);
    }

    [Fact]
    public async Task A_file_the_ignore_file_of_a_mod_names_comes_back_byte_for_byte_from_any_point()
    {
        var repo = await RepoWithModAsync();
        var first = (await _ws.CaptureAsync(repo, "profile-1", "First")).TipCommitId!;
        byte[] later = [0x01, 0x02, 0x03, 0x00, 0xFE];
        _ws.Write(repo, "mod/world.sav", later);
        await _ws.CaptureAsync(repo, "profile-1", "Second");

        await _ws.Engine.CheckoutCommitAsync(repo, first, ct: Ct);
        var atFirst = await File.ReadAllBytesAsync(_ws.FileOf(repo, "mod/world.sav"), Ct);
        await _ws.Engine.CheckoutBranchAsync(repo, "profile-1", ct: Ct);
        var atTip = await File.ReadAllBytesAsync(_ws.FileOf(repo, "mod/world.sav"), Ct);

        Assert.Equal(_binary, atFirst);
        Assert.Equal(later, atTip);
    }

    [Fact]
    public async Task A_changed_file_the_ignore_file_names_shows_as_unsaved_and_a_new_one_as_untracked()
    {
        var repo = await RepoWithModAsync();
        await _ws.CaptureAsync(repo, "profile-1");
        _ws.Write(repo, "mod/world.sav", "edited");
        _ws.Write(repo, "mod/extra.sav", "new");

        var status = await _ws.Engine.GetStatusAsync(repo, Ct);

        Assert.False(status.IsClean);
        Assert.Equal(["mod/world.sav"], status.ChangedPaths);
        Assert.Equal(["mod/extra.sav"], status.UntrackedPaths);
    }

    [Fact]
    public async Task A_deleted_file_the_ignore_file_names_shows_as_unsaved_and_is_recorded_as_deleted()
    {
        var repo = await RepoWithModAsync();
        await _ws.CaptureAsync(repo, "profile-1");
        _ws.Delete(repo, "mod/world.sav");

        var status = await _ws.Engine.GetStatusAsync(repo, Ct);
        await _ws.CaptureAsync(repo, "profile-1", "Deleted");

        Assert.Equal(["mod/world.sav"], status.ChangedPaths);
        Assert.DoesNotContain("mod/world.sav", await _ws.TreeAsync(repo, "profile-1"));
    }

    [Fact]
    public async Task Discard_removes_a_new_file_the_ignore_file_names_and_restores_a_changed_one()
    {
        var repo = await RepoWithModAsync();
        await _ws.CaptureAsync(repo, "profile-1");
        _ws.Write(repo, "mod/world.sav", "edited");
        _ws.Write(repo, "mod/extra.sav", "new");

        await _ws.Engine.DiscardChangesAsync(repo, Ct);

        Assert.False(File.Exists(_ws.FileOf(repo, "mod/extra.sav")));
        Assert.Equal(_binary, await File.ReadAllBytesAsync(_ws.FileOf(repo, "mod/world.sav"), Ct));
        Assert.True((await _ws.Engine.GetStatusAsync(repo, Ct)).IsClean);
    }

    [Fact]
    public async Task Clean_removes_a_new_file_the_ignore_file_names()
    {
        var repo = await RepoWithModAsync();
        await _ws.CaptureAsync(repo, "profile-1");
        _ws.Write(repo, "mod/extra.sav", "new");

        await _ws.Engine.CleanAsync(repo, Ct);

        Assert.False(File.Exists(_ws.FileOf(repo, "mod/extra.sav")));
    }

    [Fact]
    public async Task Previews_list_and_cost_a_file_the_ignore_file_names()
    {
        var repo = await RepoWithModAsync();

        var candidates = await _ws.Engine.ListCaptureCandidatesAsync(repo, [], Ct);
        var bytes = await _ws.Engine.EstimateNewBytesAsync(repo, candidates, [], Ct);

        Assert.Contains(candidates, candidate => candidate.Path == "mod/world.sav" && candidate.Bytes == _binary.Length);
        Assert.Equal(candidates.Sum(candidate => candidate.Bytes), bytes);
    }

    [Fact]
    public async Task The_estimate_of_a_later_capture_counts_a_new_file_the_ignore_file_names()
    {
        var repo = await RepoWithModAsync();
        await _ws.CaptureAsync(repo, "profile-1");
        _ws.Write(repo, "mod/extra.sav", "123456");
        var candidates = await _ws.Engine.ListCaptureCandidatesAsync(repo, [], Ct);

        Assert.Equal(6, await _ws.Engine.EstimateNewBytesAsync(repo, candidates, [], Ct));
    }

    [Fact]
    public async Task The_exclude_list_of_SavePoint_still_wins_over_a_mod_that_wants_a_file_kept()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "mod/.gitignore", "!cache.tmp\n");
        _ws.Write(repo, "mod/cache.tmp", "cache");
        _ws.Write(repo, "mod/keep.txt", "keep");

        await _ws.CaptureAsync(repo, "profile-1", ignore: ["*.tmp"]);

        var tree = await _ws.TreeAsync(repo, "profile-1");
        Assert.Contains("mod/keep.txt", tree);
        Assert.DoesNotContain("mod/cache.tmp", tree);
    }

    [Fact]
    public async Task A_file_named_like_a_glob_is_recorded_as_itself_and_pulls_in_nothing_else()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "[ab].txt", "glob");
        _ws.Write(repo, "a.txt", "excluded by the list");
        _ws.Write(repo, "b.txt", "excluded by the list");

        await _ws.CaptureAsync(repo, "profile-1", ignore: ["a.txt", "b.txt"]);

        Assert.Equal(["[ab].txt"], await _ws.TreeAsync(repo, "profile-1"));
    }

    [Fact]
    public async Task A_capture_with_nothing_new_records_no_excluded_file_and_not_the_marker()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "a.txt", "a");
        await _ws.CaptureAsync(repo, "profile-1", ignore: ["*.tmp"]);
        _ws.Write(repo, "cache.tmp", "cache");

        var outcome = await _ws.CaptureAsync(repo, "profile-1", ignore: ["*.tmp"]);

        Assert.False(outcome.Changed);
        Assert.Equal(["a.txt"], await _ws.TreeAsync(repo, "profile-1"));
    }

    [Fact]
    public async Task Status_still_hides_the_marker_when_the_exclude_file_has_gone_missing()
    {
        var repo = await RepoWithModAsync();
        await _ws.CaptureAsync(repo, "profile-1");
        File.Delete(Path.Combine(repo.GitDir, "info", "exclude"));

        var status = await _ws.Engine.GetStatusAsync(repo, Ct);

        Assert.True(status.IsClean);
        Assert.DoesNotContain(status.UntrackedPaths, path => path.Contains("savepoint-managed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Discard_keeps_what_patterns_with_spaces_name_and_removes_what_a_negation_lets_back_in()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "a.txt", "a");
        string[] ignore = ["Saves Backup/", "*.log", "!keep.log"];
        await _ws.CaptureAsync(repo, "profile-1", ignore: ignore);
        _ws.Write(repo, "Saves Backup/slot 1.txt", "backup");
        _ws.Write(repo, "x.log", "excluded");
        _ws.Write(repo, "keep.log", "not excluded");

        await _ws.Engine.DiscardChangesAsync(repo, Ct);

        Assert.True(File.Exists(_ws.FileOf(repo, "Saves Backup/slot 1.txt")));
        Assert.True(File.Exists(_ws.FileOf(repo, "x.log")));
        Assert.False(File.Exists(_ws.FileOf(repo, "keep.log")));
    }

    [Fact]
    public async Task Clean_and_discard_leave_a_file_the_exclude_list_names_even_when_a_mod_ignore_file_names_it_too()
    {
        var repo = await RepoWithModAsync();
        _ws.Write(repo, "mod/.gitignore", "*.sav\n*.tmp\n");
        _ws.Write(repo, "mod/cache.tmp", "cache");
        await _ws.CaptureAsync(repo, "profile-1", ignore: ["*.tmp"]);
        _ws.Write(repo, "mod/extra.sav", "new");

        await _ws.Engine.DiscardChangesAsync(repo, Ct);

        Assert.True(File.Exists(_ws.FileOf(repo, "mod/cache.tmp")));
        Assert.True(File.Exists(EngineWorkspace.MarkerPath(repo)));
        Assert.False(File.Exists(_ws.FileOf(repo, "mod/extra.sav")));
    }
}
