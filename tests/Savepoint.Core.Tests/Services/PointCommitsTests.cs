using Savepoint.Core.Git;
using Savepoint.Core.Services;

namespace Savepoint.Core.Tests.Services;

// One function says which commit of a folder stands for a point, for History and for "Go back to this" alike (D31).
public class PointCommitsTests
{
    private static readonly DateTimeOffset _noon = new(2026, 10, 20, 12, 0, 0, TimeSpan.Zero);

    // Newest first, as a lineage comes; every commit falls in the same second, so only the capture time tells them apart.
    private static CommitInfo Commit(string id, int ms, string? captureId = null) => new(id, id, _noon, captureId ?? "cap-" + id, _noon.AddMilliseconds(ms));

    [Fact]
    public void The_commit_the_event_wrote_is_the_one_even_when_later_ones_exist()
    {
        CommitInfo[] lineage = [Commit("late", 900), Commit("own", 100, "cap-own")];

        Assert.Equal("own", PointCommits.For(lineage, "cap-own", _noon.AddMilliseconds(100))!.Id);
    }

    [Fact]
    public void A_folder_the_event_did_not_touch_takes_the_latest_commit_at_or_before_it_down_to_the_millisecond()
    {
        CommitInfo[] lineage = [Commit("newer", 300), Commit("older", 100)];

        Assert.Equal("older", PointCommits.For(lineage, "cap-other", _noon.AddMilliseconds(200))!.Id);
    }

    [Fact]
    public void A_folder_whose_commits_all_came_after_the_event_has_no_commit_for_it()
    {
        CommitInfo[] lineage = [Commit("newer", 500), Commit("older", 400)];

        Assert.Null(PointCommits.For(lineage, "cap-other", _noon.AddMilliseconds(100)));
    }

    [Fact]
    public void A_commit_from_before_the_time_trailer_is_placed_by_its_date()
    {
        CommitInfo[] lineage = [new("old", "old", _noon.AddHours(-1), "cap-old", null)];

        Assert.Equal("old", PointCommits.For(lineage, "cap-other", _noon)!.Id);
    }

    [Fact]
    public void A_commit_with_no_capture_id_is_never_the_stand_in()
    {
        CommitInfo[] lineage = [new("plain", "plain", _noon.AddHours(-1), null, null)];

        Assert.Null(PointCommits.For(lineage, "cap-other", _noon));
    }
}
