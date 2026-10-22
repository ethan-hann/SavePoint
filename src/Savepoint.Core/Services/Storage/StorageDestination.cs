using Savepoint.Core.Backend;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;
using Savepoint.Core.Paths;

namespace Savepoint.Core.Services.Storage;

/// <summary>
/// The rules a folder must meet to become SavePoint's storage, which a storage move and "Find the folder" at startup both apply: it is
/// not inside the folder the installer owns, it does not overlap a game's own folders, and it is not what an unfinished move left.
/// </summary>
internal static class StorageDestination
{
    /// <summary>The file a move writes in its destination before the first copy and removes when it ends; one that is found there means an earlier move did not finish.</summary>
    internal const string MoveMarkerFileName = "savepoint-move-in-progress.txt";

    // Velopack's updater, which sits beside the folder the app runs from.
    private const string _updaterFileName = "Update.exe";

    /// <summary>The first rule the folder breaks, in the order a person would fix them; null when it breaks none.</summary>
    /// <param name="services">The backend, for the install folder and the path tokens.</param>
    /// <param name="pathSafety">Judges paths; any snapshot's judge will do, since these rules do not depend on the storage root.</param>
    /// <param name="games">The catalog's games.</param>
    /// <param name="destination">The folder, absolute.</param>
    public static SavepointException? Refusal(BackendServices services, PathSafety pathSafety, IReadOnlyList<GameDefinition> games, string destination)
    {
        if (InstallFolderOf(services.InstallDirectory, destination, pathSafety) is { } installFolder)
        {
            return new StorageInsideInstallFolderException(destination, installFolder);
        }

        foreach (var game in games)
        {
            foreach (var folder in GameFolders.ResolveLoosely(services.PathTokens, game).Where(folder => folder.Path is not null))
            {
                if (pathSafety.PathsOverlap(destination, folder.Path!))
                {
                    return new GameOverlapException(game.Id, game.Name);
                }
            }
        }

        // Checked before "not empty": the files in such a folder are SavePoint's own half-finished copy, which the user can delete.
        return File.Exists(Path.Combine(destination, MoveMarkerFileName)) ? new StorageMoveUnfinishedException(destination) : null;
    }

    // The installed app runs from "current" inside an install folder that also holds Update.exe, and an update or an uninstall deletes
    // that whole folder. Returns the install folder when the destination lies in it.
    private static string? InstallFolderOf(string installDirectory, string destination, PathSafety pathSafety)
    {
        if (Directory.GetParent(installDirectory)?.FullName is not { } installFolder || !File.Exists(Path.Combine(installFolder, _updaterFileName)))
        {
            return null;
        }

        var relative = Path.GetRelativePath(pathSafety.ResolveRealPath(installFolder), pathSafety.ResolveRealPath(destination));
        var inside = relative == "." || (!Path.IsPathRooted(relative) && relative != ".." && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal));
        return inside ? installFolder : null;
    }
}
