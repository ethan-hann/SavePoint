using Savepoint.Core.Errors;
using Savepoint.Core.Git;

namespace Savepoint.Core.Tests.Git.Engine;

public sealed class AmendTests : IDisposable
{
    private readonly EngineWorkspace _ws = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _ws.Dispose();

    [Fact]
    public async Task Amend_rewrites_the_tip_in_place_with_the_new_content_message_and_capture_id()
    {
        var repo = await _ws.InitializedRepoAsync();
        var points = await _ws.ThreePointsAsync(repo);
        _ws.Write(repo, "c.txt", "added later");

        var outcome = await _ws.Engine.CaptureAsync(repo, Amend("profile-1", "Third point, updated", "cap-new"), ct: Ct);

        Assert.True(outcome.Changed);
        Assert.NotEqual(points[2], outcome.TipCommitId);
        Assert.Equal(outcome.TipCommitId, await _ws.TipAsync(repo, "profile-1"));
        Assert.Equal(3, await _ws.CountAsync(repo, "profile-1"));
        Assert.Equal(points[1], await _ws.ResolveAsync(repo, "profile-1~1"));
        Assert.Equal("Third point, updated", await _ws.LogAsync(repo, "profile-1", "%s"));
        Assert.Equal("cap-new", await _ws.LogAsync(repo, "profile-1", "%(trailers:key=Savepoint-Capture,valueonly)"));
        Assert.Equal(["a.txt", "b.txt", "c.txt"], (await _ws.TreeAsync(repo, "profile-1")).Order());
    }

    [Fact]
    public async Task Amend_stamps_the_point_with_the_time_of_the_amend()
    {
        var repo = await _ws.InitializedRepoAsync();
        await _ws.ThreePointsAsync(repo);
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);
        await Task.Delay(1100, Ct);

        await _ws.Engine.CaptureAsync(repo, Amend("profile-1", "Updated", "cap-new"), ct: Ct);

        var date = DateTimeOffset.Parse(await _ws.LogAsync(repo, "profile-1", "%aI"), System.Globalization.CultureInfo.InvariantCulture);
        Assert.True(date > before.AddSeconds(1), $"{date} should be the time of the amend, not of the original point");
    }

    [Fact]
    public async Task Amend_of_an_unchanged_folder_still_rewrites_the_message()
    {
        var repo = await _ws.InitializedRepoAsync();
        var points = await _ws.ThreePointsAsync(repo);

        var outcome = await _ws.Engine.CaptureAsync(repo, Amend("profile-1", "Renamed", "cap-new"), ct: Ct);

        Assert.True(outcome.Changed);
        Assert.NotEqual(points[2], outcome.TipCommitId);
        Assert.Equal("Renamed", await _ws.LogAsync(repo, "profile-1", "%s"));
    }

    [Fact]
    public async Task Amend_is_not_available_when_the_branch_has_no_points()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.Write(repo, "a.txt", "a");

        var error = await Assert.ThrowsAsync<AmendNotAvailableException>(() => _ws.Engine.CaptureAsync(repo, Amend("profile-1", "x", "id"), ct: Ct));

        Assert.Equal("error.gitOperation.amendNotAvailable", error.MessageId);
        Assert.Null(await _ws.ResolveAsync(repo, "HEAD"));
    }

    [Fact]
    public async Task Amend_is_not_available_when_another_branch_is_checked_out()
    {
        var repo = await _ws.InitializedRepoAsync();
        var points = await _ws.ThreePointsAsync(repo);
        await _ws.Engine.CreateBranchAsync(repo, "profile-2", points[1], Ct);

        await Assert.ThrowsAsync<AmendNotAvailableException>(() => _ws.Engine.CaptureAsync(repo, Amend("profile-2", "x", "id"), ct: Ct));

        Assert.Equal(points[2], await _ws.TipAsync(repo, "profile-1"));
        Assert.Equal(points[1], await _ws.TipAsync(repo, "profile-2"));
    }

    [Fact]
    public async Task Amend_is_not_available_while_the_folder_is_on_an_older_point()
    {
        var repo = await _ws.InitializedRepoAsync();
        var points = await _ws.ThreePointsAsync(repo);
        await _ws.Engine.CheckoutCommitAsync(repo, points[0], ct: Ct);

        await Assert.ThrowsAsync<AmendNotAvailableException>(() => _ws.Engine.CaptureAsync(repo, Amend("profile-1", "x", "id"), ct: Ct));

        Assert.Equal(points[2], await _ws.TipAsync(repo, "profile-1"));
    }

    private static CaptureRequest Amend(string branch, string message, string captureId) =>
        new(branch, message, captureId) { Mode = CaptureMode.Amend };
}
