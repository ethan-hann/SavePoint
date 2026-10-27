using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;
using Savepoint.Core.Git;

namespace Savepoint.Core.Services.Profiles;

// A new profile may start a folder empty only when every file the folder holds now is already in a snapshot of a profile of the game.
// Starting empty leaves the files in place as new changes of the new profile, and a launch offers to discard those; without a snapshot
// of them elsewhere that would delete the player's only copy. The wizard's tick boxes, the refusal before a profile is made and the
// check inside the save all ask this, so they never disagree.
internal static class StartEmptyRule
{
    /// <summary>
    /// True when the folder sits on the branch of one of the game's profiles, has a snapshot there, and has no new changes since.
    /// A folder that does not exist holds nothing to lose. Anything that cannot be told (an unreadable file, a stopped switch, a folder
    /// SavePoint has no history for) counts as not safe.
    /// </summary>
    public static async Task<bool> IsSafeAsync(GitEngine engine, RepoRef repo, IReadOnlyCollection<string> profileBranches, CancellationToken cancellationToken) =>
        await WhyNotAsync(engine, repo, profileBranches, cancellationToken).ConfigureAwait(false) == StartEmptyBlock.None;

    /// <summary>
    /// What stands in the way of starting the folder empty, or <see cref="StartEmptyBlock.None"/> when it is safe (see <see cref="IsSafeAsync"/>).
    /// A stopped switch and a folder that cannot be checked are told apart from new changes because a snapshot is not the way out of either.
    /// </summary>
    public static async Task<StartEmptyBlock> WhyNotAsync(GitEngine engine, RepoRef repo, IReadOnlyCollection<string> profileBranches, CancellationToken cancellationToken)
    {
        try
        {
            var status = await engine.GetStatusAsync(repo, cancellationToken, waitForNote: false).ConfigureAwait(false);
            if (status.InterruptedSwitch is not null)
            {
                return StartEmptyBlock.StoppedSwitch;
            }

            if (!status.IsClean)
            {
                return StartEmptyBlock.NewChanges;
            }

            return status is { IsDetached: false, HeadCommit: not null, HeldFile: null, Branch: { } branch } && profileBranches.Contains(branch, StringComparer.Ordinal)
                ? StartEmptyBlock.None
                : StartEmptyBlock.Other;
        }
        catch (PathMissingException)
        {
            return StartEmptyBlock.None;
        }
        catch (Exception e) when (e is SavepointException or IOException)
        {
            return StartEmptyBlock.StoppedSwitch;
        }
    }

    /// <summary>The issue a refusal carries.</summary>
    public static ValidationException Refusal() => new([new ValidationIssue("startEmpty", ValidationMessageIds.ProfileStartEmptyUnsaved)]);
}
