namespace Savepoint.Core.Git;

/// <summary>A branch the caller does not count as a profile's, with the commits only it holds.</summary>
/// <param name="Name">The branch name, without <c>refs/heads/</c>.</param>
/// <param name="Tip">The commit the branch points at.</param>
/// <param name="OnlyHere">The commits the branch reaches and no counted branch does, newest first, snapshots and others alike.</param>
public sealed record StrayBranch(string Name, string Tip, IReadOnlyList<CommitInfo> OnlyHere);

/// <summary>A ref <see cref="GitEngine.ClearRemovedAsync"/> deleted, with the value it had, so <see cref="GitEngine.RestoreRemovedAsync"/> can put it back.</summary>
/// <param name="RefName">The full ref name.</param>
/// <param name="Value">The commit the ref pointed at.</param>
public sealed record ClearedRef(string RefName, string Value);
