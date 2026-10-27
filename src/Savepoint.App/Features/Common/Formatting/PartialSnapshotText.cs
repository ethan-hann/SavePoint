using Savepoint.Core.Catalog;

namespace Savepoint.App.Features.Common.Formatting;

/// <summary>The words for a snapshot that took some of a game's folders and not others.</summary>
public static class PartialSnapshotText
{
    /// <summary>
    /// One sentence for each folder, so the card says exactly which parts are in the snapshot and which are not: the folders that
    /// are in it first, then the ones that are not, each in the order the folders are shown.
    /// </summary>
    /// <param name="text">The formatter.</param>
    /// <param name="inSnapshot">The folders the snapshot holds.</param>
    /// <param name="failed">The folders it does not hold.</param>
    public static string State(UiFormatter text, IEnumerable<RepoRole> inSnapshot, IEnumerable<RepoRole> failed)
    {
        var held = inSnapshot.OrderBy(FolderRoles.Order).ToList();

        // After a sentence about what is in the snapshot, the other folders only need "yet"; on its own, a folder says where it is not.
        var missingKey = held.Count > 0 ? "launch.partial.notYet" : "launch.partial.notInSnapshot";
        var sentences = held.Select(role => text.Message("launch.partial.inSnapshot", ("folder", FolderRoles.Name(role))))
            .Concat(failed.OrderBy(FolderRoles.Order).Select(role => text.Message(missingKey, ("folder", FolderRoles.Name(role)))));
        return string.Join(' ', sentences);
    }

    /// <summary>The label of the retry button: the folder to retry when exactly one did not save, and the general wording otherwise.</summary>
    /// <param name="text">The formatter.</param>
    /// <param name="failed">The folders that did not save.</param>
    public static string RetryLabel(UiFormatter text, IReadOnlyCollection<RepoRole> failed) =>
        text.Message("launch.partial.retry", ("role", failed.Count == 1 ? FolderRoles.Name(failed.First()) : "other"));
}
