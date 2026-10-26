namespace Savepoint.Core.Git;

public sealed partial class GitEngine
{
    // The empty folders a capture records with the point, for the folder as it is now. A folder the exclude list names is left out whole.
    private async Task<IReadOnlyList<string>> ListEmptyFoldersAsync(RepoRef repo, CancellationToken ct)
    {
        var found = await Task.Run(() => EmptyFolders.Find(repo.WorkTree, ct), ct).ConfigureAwait(false);
        if (found.Count == 0)
        {
            return found;
        }

        var excluded = await ExcludedAmongAsync(repo, found, ct).ConfigureAwait(false);
        return [.. found.Where(folder => !excluded.Contains(folder) && EmptyFolders.IsSafe(folder) && EmptyFolders.ResolvesInside(repo.WorkTree, folder) && !LinkedFolders.IsInsideLink(repo.WorkTree, folder))];
    }

    // Makes again the empty folders the point was taken with. Folders are only created, never removed: one that is there stays, one the
    // exclude list names is not made, and one that cannot be made (a file of the player's stands there) is left out without an error.
    private async Task RestoreEmptyFoldersAsync(RepoRef repo, string commit, CancellationToken ct)
    {
        var result = await RunAsync(
            GitDirOnly(repo, "log", "-1", "--format=%(trailers:key=" + EmptyFolders.TrailerKey + ",valueonly)", commit, "--"),
            ct).ConfigureAwait(false);
        var line = result.StandardOutput.Split('\n').Select(text => text.Trim()).FirstOrDefault(text => text.Length > 0);
        var missing = EmptyFolders.Decode(line)
            .Where(folder => EmptyFolders.IsSafe(folder) && EmptyFolders.ResolvesInside(repo.WorkTree, folder))
            .Where(folder => !Directory.Exists(FullPath(repo, folder)))
            .ToArray();
        if (missing.Length == 0)
        {
            return;
        }

        var excluded = await ExcludedAmongAsync(repo, missing, ct).ConfigureAwait(false);
        RecheckBeforeFileChanges(repo, "make-folders");
        foreach (var folder in missing.Where(folder => !excluded.Contains(folder) && !LinkedFolders.IsInsideLink(repo.WorkTree, folder)))
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                Directory.CreateDirectory(RealFullPath(repo, folder));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                // Not ours to force: a file in the way or a name this system refuses. The rest are still made.
            }
        }
    }

    // The folders (relative, no trailing slash) among <paramref name="folders"/> that SavePoint's exclude list names, directly or through a folder above.
    // The question is asked against an empty work tree, so a .gitignore inside the game folder neither adds to the list nor takes from it.
    private async Task<HashSet<string>> ExcludedAmongAsync(RepoRef repo, IReadOnlyList<string> folders, CancellationToken ct)
    {
        ExcludeFromArgument(repo);
        // check-ignore refuses literal pathspecs, so a name that starts with a colon would read as pathspec magic; the "./" in front keeps it a name.
        var stdin = string.Concat(folders.Select(folder => "./" + folder + "/\0"));
        var invocation = Classify(repo, "check-ignore", "--no-index", "-z", "--stdin") with
        {
            Stdin = stdin,
            AllowedExitCodes = [0, 1],
        };
        var result = await RunAsync(invocation, ct).ConfigureAwait(false);
        return [.. result.SplitStandardOutputOnNul().Select(path => path.TrimEnd('/')[2..])];
    }
}
