using Savepoint.Core.Catalog;

namespace Savepoint.Core.Errors;

/// <summary>
/// A switch or a discard would write a file where the folder holds a folder with files the operation does not itself remove, or make a
/// folder where the player has a file of their own (<c>error.launch.fileFolderClash</c>). A file and a folder cannot share a path, and
/// writing one would delete the other with everything in it, including files the game's ignore list keeps. Nothing was written. The
/// player moves or deletes the file named, then tries again. Args: <c>role</c>, <c>kind</c> (<c>folderHoldsFiles</c> or
/// <c>fileInTheWay</c>), <c>folder</c> (the path both points claim, absolute), <c>name</c> (its last part) and <c>path</c> (the player's
/// file that stands in the way, absolute: one file inside the folder, or the file at the folder's path).
/// </summary>
public sealed class FileFolderClashException : SavepointException
{
    /// <summary>Creates the error.</summary>
    /// <param name="role">Which of the game's folders holds the clash.</param>
    /// <param name="kind">Whether a folder holds files or a file stands where a folder is needed.</param>
    /// <param name="folder">The path that is a file in one place and a folder in the other, absolute.</param>
    /// <param name="file">The player's file that stands in the way, absolute.</param>
    public FileFolderClashException(RepoRole role, FileFolderClashKind kind, string folder, string file)
        : base(
            "error.launch.fileFolderClash",
            kind == FileFolderClashKind.FolderHoldsFiles
                ? $"The {ErrorArgs.NameOf(role)} folder has a folder at '{folder}' that holds '{file}', which would be deleted to write the file the point holds there."
                : $"The {ErrorArgs.NameOf(role)} folder has a file at '{file}', which would be deleted to make the folder the point holds there.",
            new Dictionary<string, object?>
            {
                ["role"] = role,
                ["kind"] = kind,
                ["folder"] = folder,
                ["name"] = Path.GetFileName(Path.TrimEndingDirectorySeparator(folder)),
                ["path"] = file,
            })
    {
    }
}
