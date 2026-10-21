using Savepoint.Core.Catalog;

namespace Savepoint.Core.Api;

/// <summary>One folder's state as the launch dialog needs it.</summary>
/// <param name="Role">Which folder.</param>
/// <param name="Path">The folder's resolved path on this machine.</param>
/// <param name="MatchesTarget">True when the folder already holds exactly what the target asks for.</param>
/// <param name="Dirty">True when the folder has changes that are not saved.</param>
/// <param name="DirtyFileCount">How many files have unsaved changes; 0 when <paramref name="Dirty"/> is false.</param>
/// <param name="DirtyOwnerProfileId">
/// The profile the unsaved changes belong to. Null when the folder is not dirty and for a dirty folder no profile can claim,
/// which is when "Keep as a new profile" applies.
/// </param>
/// <param name="Switch">What switching the folder would change; null when the folder already matches the target.</param>
/// <param name="DirtyNewFileCount">
/// How many of <paramref name="DirtyFileCount"/> are new files, which a discard deletes; the rest are changed or removed files,
/// which a discard puts back. 0 when <paramref name="Dirty"/> is false.
/// </param>
public sealed record FolderLaunchState(
    RepoRole Role,
    string Path,
    bool MatchesTarget,
    bool Dirty,
    int DirtyFileCount,
    string? DirtyOwnerProfileId,
    SwitchSummary? Switch,
    int DirtyNewFileCount = 0);
