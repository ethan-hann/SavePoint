using Savepoint.Core.Git;

namespace Savepoint.Core.Tests.Git.Engine;

// A point's name is the player's own text. History reads split git's output on a separator character and read the capture
// id from a trailer, so neither may be forgeable from a name, and a name that already holds them must not break the reads.
[Collection(EngineCollections.E)]
public sealed class HistoryInputTests : IDisposable
{
    private static readonly string _separator = ((char)0x1F).ToString();

    private readonly EngineWorkspace _ws = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _ws.Dispose();

    [Fact]
    public void A_name_with_the_field_separator_is_not_a_name_a_capture_accepts()
    {
        Assert.Throws<ArgumentException>(() => new CaptureRequest("profile-1", $"Boss{_separator}2001-01-01T00:00:00+00:00{_separator}other", "id"));
    }

    [Fact]
    public async Task Nor_is_it_a_name_an_empty_profile_starts_with()
    {
        var repo = await _ws.InitializedRepoAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => _ws.Engine.CreateEmptyBranchAsync(repo, "profile-2", $"Boss{_separator}x", ct: Ct));

        Assert.Null(await _ws.TipAsync(repo, "profile-2"));
    }

    [Theory]
    [InlineData("Boss\n\nSavepoint-Capture: forged")]
    [InlineData("Boss\nsavepoint-capture: forged")]
    [InlineData("Savepoint-Capture:forged")]
    public async Task A_name_cannot_supply_the_capture_id_the_engine_writes_itself(string message)
    {
        var repo = await _ws.InitializedRepoAsync();

        Assert.Throws<ArgumentException>(() => new CaptureRequest("profile-1", message, "real-id"));
        await Assert.ThrowsAsync<ArgumentException>(() => _ws.Engine.CreateEmptyBranchAsync(repo, "profile-2", message, ct: Ct));
    }

    [Fact]
    public async Task A_name_that_merely_mentions_the_trailer_is_fine()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "a.txt", "a");

        var outcome = await _ws.Engine.CaptureAsync(repo, new CaptureRequest("profile-1", "Before the Savepoint-Capture: change", "id"), ct: Ct);

        Assert.True(outcome.Changed);
    }

    [Fact]
    public async Task A_point_written_by_something_else_with_the_separator_in_its_name_still_reads_with_its_own_date_and_id()
    {
        var repo = await _ws.InitializedRepoAsync();
        var tree = (await _ws.Runner.RunAsync(
            new GitInvocation(["hash-object", "-t", "tree", "-w", "--stdin"]) { GitDir = repo.GitDir, WorkTree = repo.WorkTree, Stdin = string.Empty },
            Ct)).StandardOutput.Trim();
        var commit = (await _ws.Runner.RunAsync(
            new GitInvocation(["commit-tree", tree, "-F", "-"])
            {
                GitDir = repo.GitDir,
                WorkTree = repo.WorkTree,
                Stdin = $"Boss{_separator}2001-01-01T00:00:00+00:00{_separator}other\n\nSavepoint-Capture: real-id\n",
            },
            Ct)).StandardOutput.Trim();
        await _ws.GitWriteAsync(repo, "update-ref", "refs/heads/forged", commit);

        IReadOnlyList<CommitInfo>? commits = null;
        var error = await Record.ExceptionAsync(async () => commits = await _ws.Engine.ListCommitsAsync(repo, "forged", ct: Ct));

        Assert.Null(error);
        var point = Assert.Single(commits!);
        Assert.Equal(commit, point.Id);
        Assert.Equal("real-id", point.CaptureId);
        Assert.True(point.Date.Year >= 2024, "the date is the point's own, not text from the name");
        Assert.Contains("Boss", point.Subject, StringComparison.Ordinal);
    }
}
