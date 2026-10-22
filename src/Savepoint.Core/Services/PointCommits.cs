using Savepoint.Core.Git;

namespace Savepoint.Core.Services;

// Which commit of a folder stands for a history point. History describes a point with it and "Go back to this" restores with it, so the two can never disagree.
internal static class PointCommits
{
    /// <summary>
    /// The commit a folder holds for a point: the one the capture event wrote, or for a folder the event did not touch the latest
    /// commit of the lineage made at or before the point. Null when the folder has none, which leaves the folder as it is (D31).
    /// </summary>
    /// <param name="lineage">The profile's commits in this folder, newest first.</param>
    /// <param name="pointId">The capture event's id.</param>
    /// <param name="pointTime">When the event happened.</param>
    public static CommitInfo? For(IReadOnlyList<CommitInfo> lineage, string pointId, DateTimeOffset pointTime)
    {
        if (lineage.FirstOrDefault(commit => commit.CaptureId == pointId) is { } own)
        {
            return own;
        }

        CommitInfo? best = null;
        foreach (var commit in lineage.Where(commit => commit.CaptureId is not null && commit.PointTime <= pointTime))
        {
            if (best is null || commit.PointTime > best.PointTime)
            {
                best = commit;
            }
        }

        return best;
    }
}
