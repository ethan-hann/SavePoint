namespace Savepoint.Core.Services.Profiles;

// When a snapshot of what a folder holds now can be added to a profile's branch in that folder. The refusal before a save and the row's
// "Take a snapshot now" availability both ask this, so the two never disagree.
internal static class SnapshotNowRule
{
    // True when the folder is on the profile's branch, or the profile has no branch in the folder, so its first snapshot there is simply what the folder holds.
    // A folder on another branch, or on an older snapshot (detached), cannot take it.
    public static bool Fits(bool branchExists, bool detached, string? checkedOutBranch, string branch) =>
        !branchExists || (!detached && string.Equals(checkedOutBranch, branch, StringComparison.Ordinal));
}
