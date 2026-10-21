using Savepoint.Core.Catalog;

namespace Savepoint.Core.Errors;

/// <summary>
/// A folder that has saved points was pointed at a place that holds few of the files its newest point recorded
/// (<c>error.gameService.folderMismatch</c>), which is what choosing the wrong folder looks like. Nothing was saved. Args:
/// <c>role</c>, <c>path</c> (the new folder), <c>presentCount</c> (recorded files found at the same relative paths) and
/// <c>recordedCount</c> (files in the newest point). Repeat the update with the folder change confirmed to go ahead.
/// </summary>
public sealed class FolderMismatchException : SavepointException
{
    /// <summary>Creates the error.</summary>
    /// <param name="role">Which of the game's folders was moved.</param>
    /// <param name="path">The folder it was pointed at.</param>
    /// <param name="presentCount">How many recorded files exist in the new folder at the same relative paths.</param>
    /// <param name="recordedCount">How many files the folder's newest point recorded.</param>
    public FolderMismatchException(RepoRole role, string path, int presentCount, int recordedCount)
        : base(
            "error.gameService.folderMismatch",
            $"The {ErrorArgs.NameOf(role)} folder '{path}' holds {presentCount} of the {recordedCount} files its newest point recorded.",
            new Dictionary<string, object?>
            {
                ["role"] = role,
                ["path"] = path,
                ["presentCount"] = presentCount,
                ["recordedCount"] = recordedCount,
            })
    {
    }
}
