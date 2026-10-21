namespace Savepoint.Core.Git;

/// <summary>What switching from one point to another would replace on disk.</summary>
/// <param name="FilesChanged">Files added, changed or removed; a removed file counts.</param>
/// <param name="BytesAtTarget">Bytes those files have at the target point; a removed file adds none.</param>
/// <param name="ChangedPaths">The changed files in git's order, relative to the work tree with forward slashes.</param>
public sealed record DiffSummary(int FilesChanged, long BytesAtTarget, IReadOnlyList<string> ChangedPaths);
