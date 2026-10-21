using Savepoint.Core.Catalog;

namespace Savepoint.Core.Api;

public sealed record FolderLaunchState(
    RepoRole Role,
    string Path,
    bool MatchesTarget,
    bool Dirty,
    int DirtyFileCount,

    // Null for a dirty folder no profile can claim, which is when "Keep as a new profile" applies.
    string? DirtyOwnerProfileId,

    // Null when the folder already matches the target.
    SwitchSummary? Switch);
