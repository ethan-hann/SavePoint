namespace Savepoint.Core.Git;

public sealed partial class GitEngine
{
    // Only SavePoint's own exclude list decides what is left out: ignore files inside the game folder (a mod cloned from a
    // code host brings its own) are never read, because a capture has to hold exactly what is on disk.
    private static string ExcludeFromArgument(RepoRef repo)
    {
        var path = RepoIgnoreFiles.ExcludePath(repo.GitDir);

        // The patterns the folder carries are the game's current ones; the file only has to say the same, whatever wrote it last.
        if (repo.IgnorePatterns is { } current)
        {
            RepoIgnoreFiles.WriteExclude(repo.GitDir, current);
        }
        else if (!File.Exists(path))
        {
            RepoIgnoreFiles.WriteExclude(repo.GitDir, []);
        }

        return "--exclude-from=" + path;
    }

    // The patterns of the exclude list, in order, for commands that take them as arguments. When the folder carries the game's current
    // patterns they are used as they are: the file was just written from them, and a read can return what a call that started
    // before an edit of the ignore list wrote since.
    private static IReadOnlyList<string> ExcludePatterns(RepoRef repo)
    {
        ExcludeFromArgument(repo);
        if (repo.IgnorePatterns is { } current)
        {
            return RepoIgnoreFiles.ExcludeLines(current);
        }

        return
        [
            .. File.ReadAllLines(RepoIgnoreFiles.ExcludePath(repo.GitDir))
                .Where(line => line.Length > 0 && line[0] != '#'),
        ];
    }

    // What a capture would stage: new files, plus tracked files that were changed or removed. A folder that is its own
    // repository is listed with a trailing slash instead of its files. A recorded file the patterns name is not staged, whatever
    // happened to it.
    // The same call also lists every recorded file, so the caller needs no second one: with -t each entry carries a tag, "?" for a new
    // file, "C" for a changed one and "R" for a removed one, and any other tag (cached, skipped, unmerged) is a recorded file.
    private async Task<(WorkTreeListing Listing, HashSet<string> Tracked)> ListForStagingAsync(RepoRef repo, CancellationToken ct)
    {
        var result = await RunAsync(Invocation(repo, readOnly: true, "ls-files", "-z", "-t", "-c", "-o", "-m", "-d", ExcludeFromArgument(repo)), ct).ConfigureAwait(false);
        var named = await RecordedNamedAsync(repo, ct).ConfigureAwait(false);
        var tracked = new HashSet<string>(StringComparer.Ordinal);
        var entries = new List<string>();
        foreach (var line in result.SplitStandardOutputOnNul())
        {
            // "<tag> <path>": the tag is one character, so the path is everything after the space that follows it.
            var path = line[2..];
            if (line[0] != '?')
            {
                tracked.Add(path);
            }

            if (line[0] is ('?' or 'C' or 'R') && !named.Contains(path))
            {
                entries.Add(path);
            }
        }

        return (WorkTreeListing.From(entries), tracked);
    }

    // New files the exclude list does not name.
    private async Task<WorkTreeListing> ListNewAsync(RepoRef repo, CancellationToken ct)
    {
        var result = await RunAsync(Invocation(repo, readOnly: true, "ls-files", "-z", "-o", ExcludeFromArgument(repo)), ct).ConfigureAwait(false);
        return WorkTreeListing.From(result.SplitStandardOutputOnNul());
    }

    // Tracked files that differ from the last point (or from nothing, before the first one): edits, removals and anything left staged.
    // A recorded file the patterns name is not a change, however it differs.
    private async Task<IReadOnlyList<string>> ListChangedAsync(RepoRef repo, bool hasCommits, CancellationToken ct)
    {
        var changed = await DiffNamesAsync(repo, hasCommits ? "HEAD" : GitDirFiles.EmptyTree, ct).ConfigureAwait(false);
        if (changed.Count == 0)
        {
            return changed;
        }

        var named = await RecordedNamedAsync(repo, ct).ConfigureAwait(false);
        return named.Count == 0 ? changed : [.. changed.Where(path => !named.Contains(path))];
    }

    // Every file in the work tree that differs from the tree, with no regard for the patterns.
    private async Task<IReadOnlyList<string>> DiffNamesAsync(RepoRef repo, string tree, CancellationToken ct)
    {
        var result = await RunAsync(Invocation(repo, readOnly: true, "diff", "--name-only", "-z", "--no-renames", tree, "--"), ct).ConfigureAwait(false);
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
