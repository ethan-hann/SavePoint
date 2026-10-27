using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;

namespace Savepoint.Core.Api;

/// <summary>One folder's state as the launch dialog needs it.</summary>
/// <param name="Role">Which folder.</param>
/// <param name="Path">The folder's resolved path on this machine.</param>
/// <param name="MatchesTarget">True when the folder already holds exactly what the target asks for.</param>
/// <param name="Dirty">True when the folder has changes that are not saved. A file the game's ignore list names is never a change, even one an earlier point recorded.</param>
/// <param name="DirtyFileCount">How many files have unsaved changes; 0 when <paramref name="Dirty"/> is false, and for a half-switched folder (<paramref name="UnfinishedSwitch"/>) in which git lists no file.</param>
/// <param name="DirtyOwnerProfileId">
/// The profile the unsaved changes belong to. Null when the folder is not dirty and for a dirty folder no profile can claim,
/// which is when "Keep as a new profile" applies.
/// </param>
/// <param name="Switch">What switching the folder would change; null when the folder already matches the target, and when the launch leaves the folder as it is because the target has no point for it.</param>
/// <param name="DirtyNewFileCount">
/// How many of <paramref name="DirtyFileCount"/> are new files, which a discard deletes; the rest are changed or removed files,
/// which a discard puts back. 0 when <paramref name="Dirty"/> is false.
/// </param>
/// <param name="UnfinishedSwitch">
/// True when an earlier switch of this folder stopped part way, so it holds files from two saves. Such a folder counts as
/// <paramref name="Dirty"/> unless the launch is the very switch that finishes it and no unsaved change stands in its way
/// (<paramref name="ChangesOutsideSwitchCount"/>), is never offered "Keep as a new profile", and
/// can always be discarded back to the save the switch started from (D31).
/// </param>
/// <param name="UnfinishedSwitchTarget">
/// A target that completes the unfinished switch when it is started; null when the folder has no unfinished switch, or when the
/// point it was heading for is not in any profile's history any more, or not known because the note that records the switch cannot be
/// read, which leaves discarding as the way out.
/// </param>
/// <param name="HeldBy">
/// Set when switching the folder would stop on a file that cannot be read, which is the only thing wrong with it: the error the launch
/// would fail with (<c>error.gitOperation.checkoutFailed</c> for a file another program holds open, <c>error.gitOperation.unreadableFile</c>
/// for one that cannot be read), with the file in its <c>path</c> argument. For a switch, a file that cannot be read is not unsaved work, so
/// it does not make the folder <paramref name="Dirty"/> (a save cannot tell such a file from a changed one, and runs on it anyway); but the folder can be both: a half-switched folder (<paramref name="UnfinishedSwitch"/>) that
/// the launch does not finish is dirty and can hold such a file too, and another folder of the same launch can be dirty while this one is
/// held. When nothing is dirty, the launch asks to try again instead of offering to keep or discard anything; otherwise unsaved work
/// comes first, since it must be resolved whatever happens to the file. A half-switched folder that the launch itself finishes is not
/// unsaved work, so it does not come first: its held file is what the launch asks about. Null when no file stands in the way.
/// </param>
/// <param name="ChangesOutsideSwitchCount">
/// For a folder whose stopped switch is the very switch this launch would finish: how many files have unsaved changes that the finish would
/// run over (files outside the ones the switch was changing, and files it removes that were edited since). Above 0 the folder counts as
/// <paramref name="Dirty"/>, <paramref name="UnfinishedSwitchTarget"/> is null (finishing is refused until they are discarded), and discarding
/// puts the folder back to the point the switch started from. 0 otherwise.
/// </param>
/// <param name="ReplacedFileCount">
/// For a folder whose stopped switch this launch would finish: how many of the files the switch was changing hold content found in neither
/// snapshot, which the finish replaces with the target's copy. A file the stopped switch left half written is counted as well as one the
/// player changed since, because the two cannot be told apart. 0 otherwise.
/// </param>
/// <param name="StoppedSwitchUnchecked">
/// True when this launch would finish the folder's stopped switch but SavePoint could not check the folder for changes that stand in its way.
/// The folder then counts as <paramref name="Dirty"/> with no <paramref name="UnfinishedSwitchTarget"/>, so discarding is the way out.
/// </param>
/// <param name="ReplacedFileNames">
/// The first of those files by name, in path order, for the saves folder (a player knows their save files by name); null for the game's folder, which can
/// hold thousands. At most <see cref="MaxNamedReplacedFiles"/> are named; <paramref name="ReplacedFileCount"/> says how many there are in all.
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
    ErrorInfo? HeldBy = null,
    int ChangesOutsideSwitchCount = 0,
    int ReplacedFileCount = 0,
    IReadOnlyList<string>? ReplacedFileNames = null,
    bool StoppedSwitchUnchecked = false)
{
    /// <summary>The most file names <see cref="ReplacedFileNames"/> carries.</summary>
    public const int MaxNamedReplacedFiles = 5;
}
