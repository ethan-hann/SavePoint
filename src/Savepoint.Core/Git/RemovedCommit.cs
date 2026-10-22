namespace Savepoint.Core.Git;

/// <summary>Why a commit left a branch.</summary>
public enum RemovalReason
{
    /// <summary>The branch was continued from an older commit, so the commits after it left the branch.</summary>
    MovedBack,

    /// <summary>The branch's newest commit was rewritten, and the commit it held before left the branch.</summary>
    Replaced,
}

/// <summary>A commit that left a branch and is kept by a record outside every branch, until the records are cleared.</summary>
/// <param name="Commit">The commit as it was when it was on the branch.</param>
/// <param name="Branch">The branch it left.</param>
/// <param name="Reason">Why it left.</param>
/// <param name="RemovedAt">When it left: the capture time of the save that removed it, to the millisecond.</param>
public sealed record RemovedCommit(CommitInfo Commit, string Branch, RemovalReason Reason, DateTimeOffset RemovedAt);
