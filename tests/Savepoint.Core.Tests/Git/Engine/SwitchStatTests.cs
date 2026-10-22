using Savepoint.Core.Git;

namespace Savepoint.Core.Tests.Git.Engine;

// What a switch would change leaves out the files the game's ignore patterns name, which it leaves alone; the history of a point
// still shows every file it holds.
[Collection(EngineCollections.D)]
public sealed class SwitchStatTests : IDisposable
{
    private readonly EngineWorkspace _ws = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _ws.Dispose();

    // First: a.txt (3 bytes) and logs/x.log (5 bytes). Second: a.txt changed to 4 bytes, logs/y.log (6 bytes) and b.txt (2 bytes) added.
    private async Task<(RepoRef Repo, string First, string Second)> TwoPointsAsync()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "a.txt", "one");
        _ws.Write(repo, "logs/x.log", "12345");
        var first = (await _ws.CaptureAsync(repo, "profile-1", "First")).TipCommitId!;
        _ws.Write(repo, "a.txt", "four");
        _ws.Write(repo, "logs/y.log", "123456");
        _ws.Write(repo, "b.txt", "bb");
        var second = (await _ws.CaptureAsync(repo, "profile-1", "Second")).TipCommitId!;
        return (repo with { IgnorePatterns = ["logs/"] }, first, second);
    }

    [Fact]
    public async Task A_switch_summary_leaves_out_the_files_the_patterns_name_and_the_history_diff_does_not()
    {
        var (repo, first, second) = await TwoPointsAsync();

        var switching = await _ws.Engine.SwitchStatAsync(repo, first, second, Ct);
        var history = await _ws.Engine.DiffStatAsync(repo, first, second, Ct);

        Assert.Equal(["a.txt", "b.txt"], switching.ChangedPaths.Order(StringComparer.Ordinal));
        Assert.Equal(4 + 2, switching.BytesAtTarget);
        Assert.Equal(["a.txt", "b.txt", "logs/y.log"], history.ChangedPaths.Order(StringComparer.Ordinal));
        Assert.Equal(4 + 2 + 6, history.BytesAtTarget);
    }

    [Fact]
    public async Task A_switch_summary_without_patterns_is_the_diff()
    {
        var (repo, first, second) = await TwoPointsAsync();

        var switching = await _ws.Engine.SwitchStatAsync(repo with { IgnorePatterns = null }, first, second, Ct);

        Assert.Equal(["a.txt", "b.txt", "logs/y.log"], switching.ChangedPaths.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task A_switch_into_a_folder_with_no_point_yet_counts_the_whole_target_less_the_files_the_patterns_name()
    {
        var (repo, first, _) = await TwoPointsAsync();

        var switching = await _ws.Engine.SwitchStatAsync(repo, null, first, Ct);

        Assert.Equal(["a.txt"], switching.ChangedPaths);
        Assert.Equal(3, switching.BytesAtTarget);
    }

    // A preview holds no lock on the game, so it can run while a switch classifies its own paths; the two must not share an index.
    [Fact]
    public async Task Two_summaries_of_one_folder_at_the_same_moment_do_not_share_their_classification_index()
    {
        var (repo, first, second) = await TwoPointsAsync();
        var nestedRan = false;
        DiffSummary? nested = null;
        _ws.OnInvoke = invocation =>
        {
            // The outer summary has put its paths in its index and is about to list them when another summary of the folder runs.
            if (!nestedRan && invocation.Arguments[0] == "ls-files" && invocation.ExtraEnvironment?.ContainsKey("GIT_INDEX_FILE") == true)
            {
                nestedRan = true;
                nested = _ws.Engine.SwitchStatAsync(repo, first, second, Ct).GetAwaiter().GetResult();
            }
        };

        var outer = await _ws.Engine.SwitchStatAsync(repo, first, second, Ct);
        _ws.OnInvoke = null;

        Assert.True(nestedRan, "the second summary never ran");
        Assert.Equal(["a.txt", "b.txt"], outer.ChangedPaths.Order(StringComparer.Ordinal));
        Assert.Equal(["a.txt", "b.txt"], nested!.ChangedPaths.Order(StringComparer.Ordinal));
        Assert.Empty(Directory.EnumerateFiles(repo.GitDir, "savepoint-classify*"));
    }

    // git reads an index file that is not there as an empty one and lists nothing, which would read as "no file is named".
    [Fact]
    public async Task A_classification_whose_index_disappears_is_not_taken_to_say_that_nothing_is_named()
    {
        var (repo, first, second) = await TwoPointsAsync();
        _ws.OnInvoke = invocation =>
        {
            if (invocation.Arguments[0] == "ls-files" && invocation.ExtraEnvironment?.GetValueOrDefault("GIT_INDEX_FILE") is { } index)
            {
                File.Delete(index);
            }
        };

        await Assert.ThrowsAsync<Savepoint.Core.Errors.GitOperationException>(() => _ws.Engine.SwitchStatAsync(repo, first, second, Ct));

        _ws.OnInvoke = null;
        Assert.Empty(Directory.EnumerateFiles(repo.GitDir, "savepoint-classify*"));
    }

    // A summary takes no lock on the game, so a write can start its sweep of stale files while the summary is asking: the file the summary
    // is asking about is not stale.
    [Fact]
    public async Task The_sweep_of_a_write_leaves_the_index_a_summary_is_still_asking_about()
    {
        var (repo, first, second) = await TwoPointsAsync();
        using var asking = new ManualResetEventSlim();
        using var swept = new ManualResetEventSlim();
        _ws.OnInvoke = invocation =>
        {
            if (invocation.Arguments[0] == "ls-files" && invocation.ExtraEnvironment?.ContainsKey("GIT_INDEX_FILE") == true)
            {
                asking.Set();
                swept.Wait(TimeSpan.FromSeconds(30), Ct);
            }
        };

        var summary = Task.Run(() => _ws.Engine.SwitchStatAsync(repo, first, second, Ct), Ct);
        Assert.True(asking.Wait(TimeSpan.FromSeconds(30), Ct), "the summary never asked");
        var removed = _ws.Engine.RemoveStaleLocks(repo.GitDir);
        swept.Set();
        var stat = await summary;

        _ws.OnInvoke = null;
        Assert.Equal(0, removed);
        Assert.Equal(["a.txt", "b.txt"], stat.ChangedPaths.Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task A_revision_that_does_not_exist_is_an_error_for_a_switch_summary()
    {
        var (repo, first, _) = await TwoPointsAsync();

        await Assert.ThrowsAsync<Savepoint.Core.Errors.GitOperationException>(() => _ws.Engine.SwitchStatAsync(repo, "ghost", first, Ct));
        await Assert.ThrowsAsync<Savepoint.Core.Errors.GitOperationException>(() => _ws.Engine.SwitchStatAsync(repo, first, new string('1', 40), Ct));
    }

    [Fact]
    public async Task The_files_of_a_point_can_be_listed_without_the_ones_the_patterns_name()
    {
        var (repo, _, second) = await TwoPointsAsync();

        var all = await _ws.Engine.ListTreeFilesAsync(repo, second, Ct);
        var restored = await _ws.Engine.ListTreeFilesAsync(repo, second, Ct, leaveOutIgnored: true);

        Assert.Equal(["a.txt", "b.txt", "logs/x.log", "logs/y.log"], all.Order(StringComparer.Ordinal));
        Assert.Equal(["a.txt", "b.txt"], restored.Order(StringComparer.Ordinal));
    }
}
