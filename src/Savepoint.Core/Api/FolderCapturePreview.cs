using Savepoint.Core.Catalog;

namespace Savepoint.Core.Api;

/// <summary>What a capture would save from one folder.</summary>
/// <param name="Role">Which folder.</param>
/// <param name="Path">The folder's resolved path on this machine.</param>
/// <param name="FileCount">Files a capture would save from the folder, ignored files left out.</param>
/// <param name="ModFileCount">How many of those are mod files; always 0 for the saves folder.</param>
/// <param name="Bytes">Total size of those files, in bytes.</param>
/// <param name="HeldInSnapshot">
/// True when every file the folder holds now is already in a snapshot of a profile of the game, or is one the game leaves out: the folder
/// sits on that profile with no new changes, and a file only a leave-out rule covers is not a change. Only then may a new profile start the folder empty (<see cref="IProfilesApi.CreateAsync"/>), since a switch to it would
/// otherwise remove files no snapshot has. False when that cannot be told, and always false for the install folder, which never starts empty.
/// </param>
/// <param name="StartEmptyBlocked">
/// Why <paramref name="HeldInSnapshot"/> is false, so the wizard can tell the player the one step that helps. <see cref="StartEmptyBlock.None"/> when
/// the folder is held, for the install folder, and for a preview that does not say.
/// </param>
/// <param name="FilesCounted">
/// False when the folder's files were not read because its consent marker is missing, belongs to another game or is damaged: the counts
/// are then 0, <paramref name="StartEmptyBlocked"/> says <see cref="StartEmptyBlock.NotConfirmed"/> for a missing marker and
/// <see cref="StartEmptyBlock.Other"/> otherwise (also for the install folder, which never starts empty), and the other folders are previewed as usual.
/// </param>
public sealed record FolderCapturePreview(RepoRole Role, string Path, int FileCount, int ModFileCount, long Bytes, bool HeldInSnapshot = false, StartEmptyBlock StartEmptyBlocked = StartEmptyBlock.None, bool FilesCounted = true);
