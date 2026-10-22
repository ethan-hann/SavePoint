using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;

namespace Savepoint.Core.Api;

/// <summary>One folder's state as the launch dialog needs it.</summary>
/// <param name="Role">Which folder.</param>
/// <param name="Path">The folder's resolved path on this machine.</param>
/// <param name="MatchesTarget">True when the folder already holds exactly what the target asks for.</param>
/// <param name="Dirty">True when the folder has changes that are not saved.</param>
/// <param name="DirtyFileCount">How many files have unsaved changes; 0 when <paramref name="Dirty"/> is false, and for a half-switched folder (<paramref name="UnfinishedSwitch"/>) in which git lists no file.</param>
/// <param name="DirtyOwnerProfileId">
/// The profile the unsaved changes belong to. Null when the folder is not dirty and for a dirty folder no profile can claim,
/// which is when "Keep as a new profile" applies.
/// </param>
/// <param name="Switch">What switching the folder would change; null when the folder already matches the target.</param>
/// <param name="DirtyNewFileCount">
/// How many of <paramref name="DirtyFileCount"/> are new files, which a discard deletes; the rest are changed or removed files,
/// which a discard puts back. 0 when <paramref name="Dirty"/> is false.
/// </param>
/// <param name="UnfinishedSwitch">
/// True when an earlier switch of this folder stopped part way, so it holds files from two saves. Such a folder counts as
/// <paramref name="Dirty"/> unless the launch is the very switch that finishes it, is never offered "Keep as a new profile", and
/// can always be discarded back to the save the switch started from (D31).
/// </param>
/// <param name="UnfinishedSwitchTarget">
/// A target that completes the unfinished switch when it is started; null when the folder has no unfinished switch, or when the
/// point it was heading for is not in any profile's history any more, which leaves discarding as the way out.
/// </param>
/// <param name="HeldBy">
/// Set when switching the folder would stop on a file that cannot be read, which is the only thing wrong with it: the error the launch
/// would fail with (<c>error.gitOperation.checkoutFailed</c> for a file another program holds open, <c>error.gitOperation.unreadableFile</c>
/// for one that cannot be read), with the file in its <c>path</c> argument. Such a folder is not <paramref name="Dirty"/>; the launch
/// asks to try again instead of offering to keep or discard anything (D37). Null when no file stands in the way.
/// </param>
public sealed record FolderLaunchState(
    RepoRole Role,
    string Path,
    bool MatchesTarget,
    bool Dirty,
    int DirtyFileCount,
    string? DirtyOwnerProfileId,
    SwitchSummary? Switch,
    int DirtyNewFileCount = 0,
    bool UnfinishedSwitch = false,
    LaunchTarget? UnfinishedSwitchTarget = null,
    ErrorInfo? HeldBy = null);
