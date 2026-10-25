using Savepoint.Core.Git;

namespace Savepoint.Core.Services.Profiles;

/// <summary>What replacing a snapshot does to one folder that has something unsaved.</summary>
internal enum ReplaceFolder
{
    /// <summary>The snapshot's commit is the profile's newest here and no other profile reaches it: it can be rewritten.</summary>
    Amend,

    /// <summary>The profile has no commit of the snapshot here: the unsaved files join it as a new commit and nothing is rewritten.</summary>
    Join,

    /// <summary>Rewriting would reach history another profile holds, or the snapshot is not the newest here.</summary>
    Refuse,
}

/// <summary>
/// The one rule for replacing a snapshot after play, used when the choice is offered and again when the save runs: replacing is safe when
/// no written folder refuses and at least one amends, because with nothing rewritten it would only be a new snapshot.
/// </summary>
internal static class ReplaceRule
{
    /// <summary>Judges one folder for the snapshot with the given capture id.</summary>
    /// <param name="engine">The git engine.</param>
    /// <param name="repo">The folder.</param>
    /// <param name="branch">The played profile's branch.</param>
    /// <param name="otherBranches">Every other profile's branch.</param>
    /// <param name="captureId">The capture id of the snapshot being replaced.</param>
    /// <param name="cancellationToken">Cancels the check.</param>
    public static async Task<ReplaceFolder> JudgeFolderAsync(GitEngine engine, RepoRef repo, string branch, IReadOnlyCollection<string> otherBranches, string captureId, CancellationToken cancellationToken)
    {
        var lineage = await engine.ListCommitsAsync(repo, branch, null, cancellationToken).ConfigureAwait(false);
        var at = lineage.ToList().FindIndex(commit => string.Equals(commit.CaptureId, captureId, StringComparison.Ordinal));
        if (at < 0)
        {
            return ReplaceFolder.Join;
        }

        if (at > 0)
        {
            return ReplaceFolder.Refuse;
        }

        // The newest commit that no other profile reaches is the tip only when the tip itself is unshared.
        var unshared = await engine.ListCommitsOnlyOnAsync(repo, branch, otherBranches, 1, cancellationToken).ConfigureAwait(false);
        return unshared.Count == 1 && unshared[0].Id == lineage[0].Id ? ReplaceFolder.Amend : ReplaceFolder.Refuse;
    }

    /// <summary>True when the judged folders allow a replacement: none refuses and at least one is rewritten.</summary>
    /// <param name="judged">The verdict of every folder that has something unsaved.</param>
    public static bool Allows(IReadOnlyCollection<ReplaceFolder> judged) =>
        !judged.Contains(ReplaceFolder.Refuse) && judged.Contains(ReplaceFolder.Amend);
}
