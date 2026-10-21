using Savepoint.Core.Errors;

namespace Savepoint.Core.Git;

// A folder with its own .git cannot be stored inside another history: git records an empty pointer for a valid one and fails
// on one without a commit, and it never stores anything named .git. Capture and preview refuse instead of saving half of it.
internal static class NestedRepositories
{
    // Throws when the listing holds a folder that keeps its own history. Nothing has been staged yet.
    public static void RefuseAny(string workTree, WorkTreeListing listing, CancellationToken ct)
    {
        var found = Find(workTree, listing, ct);
        if (found.Count > 0)
        {
            throw new NestedRepositoryException(found[0], found.Count);
        }
    }

    // The hidden entries that mark a nested repository, as absolute paths in the order of the folders that hold them.
    // git itself reports a valid repository as a folder with a trailing slash; a hidden file that points nowhere, or the
    // game folder's own .git, is invisible to it, so every folder holding something to record is looked at as well.
    internal static IReadOnlyList<string> Find(string workTree, WorkTreeListing listing, CancellationToken ct)
    {
        var folders = new HashSet<string>(StringComparer.Ordinal) { string.Empty };
        foreach (var file in listing.Files)
        {
            for (var slash = file.IndexOf('/', StringComparison.Ordinal); slash >= 0; slash = file.IndexOf('/', slash + 1))
            {
                folders.Add(file[..slash]);
            }
        }

        var reported = listing.NestedRepositories.Select(folder => folder.TrimEnd('/')).ToHashSet(StringComparer.Ordinal);
        var toCheck = folders.Where(folder => !reported.Contains(folder)).ToArray();
        var hasDot = new bool[toCheck.Length];
        Parallel.For(
            0,
            toCheck.Length,
            new ParallelOptions { CancellationToken = ct, MaxDegreeOfParallelism = Environment.ProcessorCount },
            i => hasDot[i] = Path.Exists(Path.Combine(workTree, toCheck[i].Replace('/', Path.DirectorySeparatorChar), ".git")));

        reported.UnionWith(toCheck.Where((_, i) => hasDot[i]));
        return
        [
            .. reported
                .Order(StringComparer.Ordinal)
                .Select(folder => Path.Combine(workTree, folder.Replace('/', Path.DirectorySeparatorChar), ".git")),
        ];
    }
}
