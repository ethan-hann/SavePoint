using Savepoint.Core.Git;

namespace Savepoint.Core.Tests.Git.Engine;

// Every point records when its capture event began, to the millisecond, so two points in one second keep their order (D31).
[Collection(EngineCollections.B)]
public sealed class CaptureTimeTests : IDisposable
{
    private static readonly DateTimeOffset _start = new(2026, 10, 20, 9, 0, 0, 123, TimeSpan.Zero);

    private readonly FixedClock _clock = new(_start);
    private readonly EngineWorkspace _ws;

    public CaptureTimeTests()
    {
        _ws = new EngineWorkspace(options => options with { Clock = _clock });
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _ws.Dispose();

    private async Task<IReadOnlyList<CommitInfo>> CommitsAsync(RepoRef repo) => await _ws.Engine.ListCommitsAsync(repo, "profile-1", null, Ct);

    [Fact]
    public async Task A_capture_records_the_engine_clock_to_the_millisecond_and_two_captures_in_one_second_stay_in_order()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "a.txt", "one");
        await _ws.CaptureAsync(repo, "profile-1", "First");
        _clock.Set(_start.AddMilliseconds(40));
        _ws.Write(repo, "b.txt", "two");
        await _ws.CaptureAsync(repo, "profile-1", "Second");

        var commits = await CommitsAsync(repo);

        Assert.Equal([_start.AddMilliseconds(40), _start], commits.Select(commit => commit.CapturedAt));
        Assert.True(commits[0].PointTime > commits[1].PointTime);
    }

    [Fact]
    public async Task A_time_given_by_the_caller_wins_over_the_clock_so_every_folder_of_an_event_agrees()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "a.txt", "one");
        var began = _start.AddMinutes(-5).AddMilliseconds(7);

        await _ws.Engine.CaptureAsync(repo, new CaptureRequest("profile-1", "First", "cap-1") { CapturedAt = began }, ct: Ct);

        Assert.Equal(began, Assert.Single(await CommitsAsync(repo)).CapturedAt);
    }

    [Fact]
    public async Task An_amend_records_the_time_of_the_amend()
    {
        var (repo, _) = await _ws.ThreePointsRepoAsync();
        _clock.Set(_start.AddMinutes(10));
        _ws.Write(repo, "c.txt", "added later");

        await _ws.Engine.CaptureAsync(repo, new CaptureRequest("profile-1", "Updated", "cap-new") { Mode = CaptureMode.Amend }, ct: Ct);

        Assert.Equal(_start.AddMinutes(10), (await CommitsAsync(repo))[0].CapturedAt);
    }

    [Fact]
    public async Task An_empty_profile_branch_carries_the_time_it_was_given()
    {
        var repo = await _ws.InitializedRepoAsync();

        await _ws.Engine.CreateEmptyBranchAsync(repo, "profile-1", "Profile started", "cap-empty", _start.AddSeconds(3), Ct);

        Assert.Equal(_start.AddSeconds(3), Assert.Single(await CommitsAsync(repo)).CapturedAt);
    }

    [Fact]
    public void A_point_name_cannot_pass_itself_off_as_the_time_trailer()
    {
        var error = Assert.Throws<ArgumentException>(() => new CaptureRequest("profile-1", "A name\nSavepoint-Captured-At: 1", "cap-1"));

        Assert.Equal("message", error.ParamName);
    }
}
