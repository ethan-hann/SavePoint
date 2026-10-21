namespace Savepoint.Core.Git;

public sealed partial class GitEngine
{
    // Only SavePoint's own exclude list decides what is left out: ignore files inside the game folder (a mod cloned from a
    // code host brings its own) are never read, because a capture has to hold exactly what is on disk.
    private static string ExcludeFromArgument(RepoRef repo)
    {
        var path = RepoIgnoreFiles.ExcludePath(repo.GitDir);
        if (!File.Exists(path))
        {
            RepoIgnoreFiles.WriteExclude(repo.GitDir, []);
        }

        return "--exclude-from=" + path;
    }

    // The patterns of the exclude list, in order, for commands that take them as arguments.
    private static IReadOnlyList<string> ExcludePatterns(RepoRef repo)
    {
        ExcludeFromArgument(repo);
        return
        [
            .. File.ReadAllLines(RepoIgnoreFiles.ExcludePath(repo.GitDir))
                .Where(line => line.Length > 0 && line[0] != '#'),
        ];
    }

    // What a capture would stage: new files, plus tracked files that were changed or removed. A folder that is its own
    // repository is listed with a trailing slash instead of its files.
    private async Task<WorkTreeListing> ListForStagingAsync(RepoRef repo, CancellationToken ct)
    {
        var result = await RunAsync(Invocation(repo, readOnly: true, "ls-files", "-z", "-o", "-m", "-d", ExcludeFromArgument(repo)), ct).ConfigureAwait(false);
        return WorkTreeListing.From(result.SplitStandardOutputOnNul());
    }

    // New files the exclude list does not name.
    private async Task<WorkTreeListing> ListNewAsync(RepoRef repo, CancellationToken ct)
    {
        var result = await RunAsync(Invocation(repo, readOnly: true, "ls-files", "-z", "-o", ExcludeFromArgument(repo)), ct).ConfigureAwait(false);
        return WorkTreeListing.From(result.SplitStandardOutputOnNul());
    }

    // Tracked files that differ from the last point (or from nothing, before the first one): edits, removals and anything left staged.
    private async Task<IReadOnlyList<string>> ListChangedAsync(RepoRef repo, bool hasCommits, CancellationToken ct)
    {
        var result = await RunAsync(
            Invocation(repo, readOnly: true, "diff", "--name-only", "-z", "--no-renames", hasCommits ? "HEAD" : GitDirFiles.EmptyTree, "--"),
            ct).ConfigureAwait(false);
        return result.SplitStandardOutputOnNul();
    }
}

// The entries of an ls-files listing: files, and the folders that are repositories of their own (shown with a trailing slash).
internal sealed record WorkTreeListing(IReadOnlyList<string> Files, IReadOnlyList<string> NestedRepositories)
{
    public static WorkTreeListing From(IEnumerable<string> entries)
    {
        var files = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var nested = new List<string>();
        foreach (var entry in entries)
        {
            if (entry.EndsWith('/'))
            {
                nested.Add(entry);
            }
            else if (seen.Add(entry))
            {
                files.Add(entry);
            }
        }

        return new WorkTreeListing(files, nested);
    }
}
