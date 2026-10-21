using Savepoint.Core.Api;
using Savepoint.Core.Backend;
using Savepoint.Core.Catalog;
using Savepoint.Core.Git;
using Savepoint.Core.Paths;

namespace Savepoint.Core.Services.Games;

/// <summary>A folder whose consent marker names a game that is no longer in the catalog.</summary>
/// <param name="FolderRole">Which folder of the game being added the marker was found in.</param>
/// <param name="WorkTree">The folder.</param>
/// <param name="MarkerGameId">The removed game the marker names.</param>
/// <param name="MarkerRole">The role the marker gives the folder.</param>
/// <param name="Branches">The branches the removed game's git directory still holds; empty when there is no history to save.</param>
internal sealed record Orphan(RepoRole FolderRole, string WorkTree, string MarkerGameId, RepoRole MarkerRole, IReadOnlyList<string> Branches)
{
    public bool HasHistory => Branches.Count > 0;
}

/// <summary>Finds history a removed game left at the folders of a game being added (the "found saved history" question).</summary>
internal static class OrphanScanner
{
    private const string _branchPrefix = "profile/";

    /// <summary>
    /// The orphans among the folders. A marker of a game that is still in the catalog is a live claim, not an orphan; the engine
    /// reports it when the repository is created. Nothing is changed.
    /// </summary>
    public static async Task<IReadOnlyList<Orphan>> FindAsync(
        StorageSnapshot storage,
        string gameId,
        IReadOnlyList<GameFolder> folders,
        IReadOnlyCollection<string> catalogIds,
        CancellationToken cancellationToken)
    {
        var found = new List<Orphan>();
        foreach (var folder in folders)
        {
            if (ConsentMarker.Read(folder.Path!) is not ConsentMarkerState.Valid { Marker: var marker }
                || marker.GameId == gameId
                || catalogIds.Contains(marker.GameId, StringComparer.Ordinal))
            {
                continue;
            }

            var oldRepo = new RepoRef(storage.GitDirectory(marker.GameId, marker.Role), folder.Path!, marker.GameId, marker.Role);
            var branches = await storage.Engine.ListBranchesAsync(oldRepo, cancellationToken).ConfigureAwait(false);
            found.Add(new Orphan(folder.Role, folder.Path!, marker.GameId, marker.Role, branches));
        }

        return found;
    }

    /// <summary>What the dialog offers: the removed game the history belongs to, the folders that hold it, and its profiles.</summary>
    public static OrphanedHistory Describe(IReadOnlyList<Orphan> orphans)
    {
        var withHistory = orphans.Where(orphan => orphan.HasHistory).ToList();

        // One game's history at a time: the install folder's owner decides when two removed games left history in the two folders.
        var canonical = (withHistory.FirstOrDefault(orphan => orphan.FolderRole == RepoRole.Install) ?? withHistory[0]).MarkerGameId;
        var same = withHistory.Where(orphan => orphan.MarkerGameId == canonical).ToList();
        var branches = same.SelectMany(orphan => orphan.Branches).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        return new OrphanedHistory(
            canonical,
            [.. same.Select(orphan => orphan.FolderRole).Distinct().Order()],
            [.. branches.Select(branch => new RecoverableProfile(branch, SuggestName(branch)))]);
    }

    /// <summary>Lets go of the marker of every orphan, so the folder can be claimed by a game being added. The old git directory is left alone.</summary>
    public static void Release(IEnumerable<Orphan> orphans)
    {
        foreach (var orphan in orphans)
        {
            ConsentMarker.Remove(orphan.WorkTree, orphan.MarkerGameId, orphan.MarkerRole);
        }
    }

    /// <summary>
    /// A profile name read back from its branch, which is named after the profile ("profile/modded-playthrough-x7k2"); null when
    /// nothing readable is left once the prefix and the random suffix are taken off.
    /// </summary>
    public static string? SuggestName(string branch)
    {
        var id = branch.StartsWith(_branchPrefix, StringComparison.Ordinal) ? branch[_branchPrefix.Length..] : branch;
        var cut = id.LastIndexOf('-');
        var slug = cut > 0 && id.Length - cut - 1 == 4 ? id[..cut] : id;
        var words = slug.Split(['-', '_', '/'], StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0 || (words.Length == 1 && words[0] is "profile" or "game"))
        {
            return null;
        }

        var text = string.Join(' ', words);
        return char.ToUpperInvariant(text[0]) + text[1..];
    }

    /// <summary>The id a recovered branch becomes: the branch without its "profile/" prefix.</summary>
    public static string ProfileIdOf(string branch) =>
        (branch.StartsWith(_branchPrefix, StringComparison.Ordinal) ? branch[_branchPrefix.Length..] : branch).Replace('/', '-');
}
