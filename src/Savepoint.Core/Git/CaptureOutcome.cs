namespace Savepoint.Core.Git;

/// <summary>The result of a capture.</summary>
/// <param name="Changed">
/// True when the capture recorded a new commit; false when the folder already matched the branch ("nothing changed"). A
/// fork or a move forward can still have created or moved a branch when this is false, so a caller that decides whether a
/// profile exists checks <see cref="TipCommitId"/> and the branch, not this flag alone.
/// </param>
/// <param name="TipCommitId">The full id of the commit the branch points at afterwards; null when the repository still has no commit at all.</param>
public sealed record CaptureOutcome(bool Changed, string? TipCommitId);
