using Savepoint.Core.Catalog;
using Savepoint.Core.Git;

namespace Savepoint.Core.Launch.Orchestration;

// One folder as a launch finds it: where git says it is, set against where the launch wants it.
internal sealed record FolderReading(FolderTarget Target, RepoStatus Status, IReadOnlyList<Profile> Profiles)
{
    public InterruptedSwitch? Note => Status.InterruptedSwitch;

    // A half-finished switch to this very target is what the launch carries on with; it is not the player's work.
    public bool Pending => Note is not null && Note.ToCommit == Target.Commit;

    // A half-finished switch to something else cannot be told apart from work the player did, so it blocks like unsaved changes.
    public bool Blocked => Note is not null && !Pending;

    // The folder holds files from two points, whether this launch finishes the switch or not.
    public bool UnfinishedSwitch => Note is not null;

    public bool Dirty => Blocked || (Note is null && !Status.IsClean);

    // The files a discard would delete; the rest of the unsaved files are put back.
    public int DirtyNewFileCount => Dirty ? Status.UntrackedPaths.Count : 0;

    // A blocked folder counts as one thing to resolve even when git lists no file.
    public int DirtyFileCount => Dirty ? Math.Max(1, Status.ChangedPaths.Count + Status.UntrackedPaths.Count) : 0;

    // Changes on a folder left on an older point, or on the branch of a profile that is gone, belong to nobody.
    public string? DirtyOwnerProfileId =>
        Dirty && !Blocked && !Status.IsDetached ? Profiles.FirstOrDefault(profile => profile.BranchName == Status.Branch)?.Id : null;

    // The folder is on the commit the launch wants, attached the way it wants (to the branch for a tip, detached for an older point).
    public bool InPlace => Target.Leave || (Target.Branch is { } branch
        ? !Status.IsDetached && Status.Branch == branch && Status.HeadCommit == Target.Commit
        : Status.IsDetached && Status.HeadCommit == Target.Commit);

    // Holds exactly what the target asks for, with nothing unsaved and no switch left unfinished.
    public bool Matches => InPlace && Status.IsClean && Note is null;

    // A file that cannot be read stands in the way of a switch; a folder that needs none carries on with it as it is.
    public bool Held => NeedsSwitch && Status.HeldFile is not null;

    // The launch must run a switch on this folder. A folder with unsaved changes never gets this far, so a clean folder that is in
    // place needs none.
    public bool NeedsSwitch => !InPlace || Note is not null;

    public static async Task<FolderReading> ReadAsync(GitEngine engine, FolderTarget target, IReadOnlyList<Profile> profiles, CancellationToken cancellationToken)
    {
        var status = await engine.GetStatusAsync(target.Repo, cancellationToken).ConfigureAwait(false);

        // A folder nobody has saved yet holds nothing a launch could lose; its files are whatever the game keeps there, and the next save picks them up.
        if (target.Leave && status.HeadCommit is null)
        {
            status = status with { IsClean = true, ChangedPaths = [], UntrackedPaths = [] };
        }

        return new FolderReading(target, status, profiles);
    }
}
