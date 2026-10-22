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
