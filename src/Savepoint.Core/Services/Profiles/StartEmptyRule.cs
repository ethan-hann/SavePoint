using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;
using Savepoint.Core.Git;

namespace Savepoint.Core.Services.Profiles;

// A new profile may start a folder empty only when every file the folder holds now is already in a snapshot of a profile of the game,
// or can never be removed by a switch. Starting empty leaves the files in place as new changes of the new profile, and a launch offers
// to discard those; without a snapshot of them elsewhere that would delete the player's only copy. The wizard's tick boxes, the
// refusal before a profile is made and the check inside the save all ask this, so they never disagree.
internal static class StartEmptyRule
{
    /// <summary>
    /// What stands in the way of starting the folder empty, or <see cref="StartEmptyBlock.None"/> when it is safe (D65.2). The folder is safe
    /// exactly when one of these holds:
    /// <list type="bullet">
    /// <item>It does not exist: there is nothing to lose. The save that follows fails for that folder and a retry runs this check again.</item>
    /// <item>
    /// It sits on the branch of one of the game's profiles (not on an older point), that branch has a snapshot, no switch of the folder
    /// stopped part way, no file in it is new or changed since that snapshot, and git could read every file. Files the game leaves out
    /// never count as changes: no snapshot holds them, but a switch never removes them either.
    /// </item>
    /// </list>
    /// Anything else is a block. <see cref="StartEmptyBlock.StoppedSwitch"/> is a switch that stopped part way;
    /// <see cref="StartEmptyBlock.NewChanges"/> is a new or changed file, or a folder without SavePoint's marker (its files are in no
    /// snapshot); <see cref="StartEmptyBlock.Other"/> is a file that is held or unreadable, any other failure reading the folder, or a
    /// folder on an older point or on no profile of the game. A snapshot is the way out of <see cref="StartEmptyBlock.NewChanges"/> only.
    /// </summary>
    /// <param name="engine">The storage engine.</param>
    /// <param name="repo">The folder, carrying the ignore patterns it leaves out now.</param>
    /// <param name="profileBranches">The branch of every profile of the game.</param>
    /// <param name="cancellationToken">Cancels the check.</param>
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
        catch (ConsentMarkerException)
        {
            // A folder SavePoint never marked, or one the player made again, holds files no snapshot has.
            return StartEmptyBlock.NewChanges;
        }
        catch (Exception e) when (e is SavepointException or IOException)
        {
            // Not a stopped switch: finishing one would not help, and the folder could not be read at all.
            return StartEmptyBlock.Other;
        }
    }

    /// <summary>The refusal for a folder that is not safe: the issue says to take a snapshot for new changes, and that the folder cannot be checked for any other block.</summary>
    /// <param name="block">What <see cref="WhyNotAsync"/> found; not <see cref="StartEmptyBlock.None"/>.</param>
    public static ValidationException Refusal(StartEmptyBlock block) =>
        new([new ValidationIssue("startEmpty", block is StartEmptyBlock.StoppedSwitch or StartEmptyBlock.Other ? ValidationMessageIds.ProfileStartEmptyUnchecked : ValidationMessageIds.ProfileStartEmptyUnsaved)]);
}
