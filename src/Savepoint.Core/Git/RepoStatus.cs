namespace Savepoint.Core.Git;

/// <summary>What git sees in a tracked folder right now.</summary>
/// <param name="IsClean">True when nothing differs from the checked-out point and there are no new files (ignored files do not count).</param>
/// <param name="Branch">The branch HEAD is on; null when HEAD is detached on an older point. A repository with no point yet names the branch it will start.</param>
/// <param name="IsDetached">True when the folder is on an older point rather than on a branch.</param>
/// <param name="HeadCommit">The full id of the checked-out point; null when the repository has none yet.</param>
/// <param name="ChangedPaths">Tracked files that were modified, deleted or staged, relative to the work tree with forward slashes.</param>
/// <param name="UntrackedPaths">New files that are not ignored, in the same form.</param>
/// <param name="InterruptedSwitch">Set while a checkout has started and not finished; the changes listed may be half of a switch, not the player's work.</param>
public sealed record RepoStatus(
    bool IsClean,
    string? Branch,
    bool IsDetached,
    string? HeadCommit,
    IReadOnlyList<string> ChangedPaths,
    IReadOnlyList<string> UntrackedPaths,
    InterruptedSwitch? InterruptedSwitch = null);
