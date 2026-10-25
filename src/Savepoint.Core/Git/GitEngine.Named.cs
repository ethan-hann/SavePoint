using System.Text;
using Savepoint.Core.Errors;

namespace Savepoint.Core.Git;

// A file the game's patterns name is outside what SavePoint records or restores, whether or not an earlier point holds it. A switch
// or a discard leaves it exactly as it is on disk, a save stops recording it, and it is never unsaved work. git itself knows nothing
// of this for a file it tracks, so the engine keeps git away from those paths: the index is made to agree with the target for them
// before a switch, which git reads as "nothing to do here".
public sealed partial class GitEngine
{
    // The index that answers which paths a pattern names. Each question gets an index of its own: a switch asks under the game's lock, but
    // the launch dialog asks without it, so two questions about one folder can run at once and must not share a file. A stop can leave one
    // behind, and the stale-lock sweep removes every file with this prefix.
    internal const string ClassifyIndexPrefix = "savepoint-classify-";

    // The list of patterns a question asks against sits beside its index. It ends in .tmp, which a git directory being deleted counts as
    // its own, as it does a lock file.
    internal static string ExcludeFileOf(string indexFile) => indexFile + ".exclude.tmp";

    // A blob id nothing has to hold: the index entries built for the question only need a path.
    private const string _anyBlob = "e69de29bb2d1d6434b8b29ae775ad8c2e48c5391";

    // The consent marker is the exclude list's own last line; with no line besides it no file can be named, and the calls below are skipped.
    private static bool HasPatterns(RepoRef repo) => ExcludePatterns(repo).Count > 1;

    // The recorded files the patterns name. At rest the index holds exactly the checked-out point, so this one listing answers for the
    // status, a save and a discard.
    private async Task<HashSet<string>> RecordedNamedAsync(RepoRef repo, CancellationToken ct)
    {
        if (!HasPatterns(repo))
        {
            return [];
        }

        var listed = await RunAsync(Classify(repo, "ls-files", "-z", "-c", "-i", ExcludeFromArgument(repo)), ct).ConfigureAwait(false);
        return [.. listed.SplitStandardOutputOnNul()];
    }

    // A pattern for a folder (a trailing slash) names an entry only when the path is a folder, and for a file the index holds git settles
    // that by looking at the work tree. So the answer for a file would follow whatever the player's disk holds at that path, which is the
    // opposite of what the patterns say. The question is asked of a work tree that holds nothing, so an entry is the kind its mode says.
    private GitInvocation Classify(RepoRef repo, params string[] arguments)
    {
        var empty = _runner.Binary.NoHooksPath;
        Directory.CreateDirectory(empty);
        return Invocation(repo, readOnly: true, arguments) with { WorkTree = empty, WorkingDirectory = empty };
    }

    // Which of these paths the patterns name, whether or not the path exists on disk or in the index: git lists a throwaway index of
    // just these paths against the same exclude list every other listing uses. An index cannot hold a file and a folder of one name,
    // and git drops one of them without a word, so the paths go into as many indexes as it takes to keep such a pair apart. Without a
    // pair (nearly always) that is one.
    private async Task<HashSet<string>> NamedAmongAsync(RepoRef repo, IReadOnlyCollection<string> paths, CancellationToken ct)
    {
        if (paths.Count == 0 || !HasPatterns(repo))
        {
            return [];
        }

        var named = new HashSet<string>(StringComparer.Ordinal);
        foreach (var group in WithoutFileAndFolderPairs(paths))
        {
            named.UnionWith(await NamedInOneIndexAsync(repo, group, ct).ConfigureAwait(false));
        }

        return named;
    }

    // Case is ignored when pairing, since an index on a case-insensitive file system treats "Mods/foo" and "Mods/Foo/x.txt" as a pair.
    internal static List<List<string>> WithoutFileAndFolderPairs(IEnumerable<string> paths)
    {
        var groups = new List<(List<string> Paths, HashSet<string> Members, HashSet<string> Folders)>();
        foreach (var path in paths.Order(StringComparer.Ordinal))
        {
            var folders = LinkedFolders.FoldersOf([path]);
            var home = groups.FindIndex(group => !group.Folders.Contains(path) && !folders.Any(group.Members.Contains));
            if (home < 0)
            {
                groups.Add(([], new HashSet<string>(StringComparer.OrdinalIgnoreCase), new HashSet<string>(StringComparer.OrdinalIgnoreCase)));
                home = groups.Count - 1;
            }

            groups[home].Paths.Add(path);
            groups[home].Members.Add(path);
            groups[home].Folders.UnionWith(folders);
        }

        return [.. groups.Select(group => group.Paths)];
    }

    private async Task<HashSet<string>> NamedInOneIndexAsync(RepoRef repo, IEnumerable<string> paths, CancellationToken ct)
    {
        var index = Path.Combine(repo.GitDir, $"{ClassifyIndexPrefix}{Guid.NewGuid():N}.index");

        var exclude = ExcludeFileOf(index);

        // The sweep of stale files must not take a file a question is still asking about.
        _classifying[index] = 0;
        _classifying[exclude] = 0;
        try
        {
            var environment = new Dictionary<string, string?> { ["GIT_INDEX_FILE"] = index };
            var entries = new StringBuilder();
            foreach (var path in paths)
            {
                entries.Append("100644 ").Append(_anyBlob).Append('\t').Append(path).Append('\0');
            }

            await RunAsync(Invocation(repo, readOnly: false, "update-index", "-z", "--add", "--index-info") with { Stdin = entries.ToString(), ExtraEnvironment = environment }, ct).ConfigureAwait(false);
            RequireIndex(index);
            var listed = await RunAsync(Classify(repo, "ls-files", "-z", "-c", "-i", ExcludeForQuestion(repo, exclude)) with { ExtraEnvironment = environment }, ct).ConfigureAwait(false);
            RequireIndex(index);
            return [.. listed.SplitStandardOutputOnNul()];
        }
        finally
        {
            _classifying.TryRemove(index, out _);
            _classifying.TryRemove(exclude, out _);
            TryDelete(index);
            TryDelete(index + ".lock");
            TryDelete(exclude);
        }
    }

    // The list in the git directory is shared by every reader, and a reader that holds the game from before an ignore-list edit writes the
    // older list into it. A question that carries the patterns asks against a file of its own, so no other reader can change its answer.
    private static string ExcludeForQuestion(RepoRef repo, string file)
    {
        if (repo.IgnorePatterns is not { } current)
        {
            return ExcludeFromArgument(repo);
        }

        File.WriteAllText(file, RepoIgnoreFiles.ExcludeText(current), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        return "--exclude-from=" + file;
    }

    // A switch marks the entries of the files the patterns name skip-worktree while it runs: git then neither writes them (a forced
    // checkout would) nor reads them in the checkout, the refresh and the verify diff. The bit means nothing at rest, so it is not left behind.
    private async Task SetSkipWorktreeAsync(RepoRef repo, bool on, IReadOnlyCollection<string> paths, CancellationToken ct, IReadOnlyDictionary<string, string?>? environment = null)
    {
        if (paths.Count > 0)
        {
            await RunWriteAsync(
                () => Invocation(repo, readOnly: false, "update-index", on ? "--skip-worktree" : "--no-skip-worktree", "-z", "--stdin") with { Stdin = NulList(paths), ExtraEnvironment = environment },
                ct).ConfigureAwait(false);
        }
    }

    // SavePoint never wants the bit at rest, so every entry that carries it is left over from a switch that was killed (a crash or a power
    // loss) or could not take it off. Left alone it hides the file from every later comparison and from a save. Asked of the index only.
    private async Task ClearSkipWorktreeAsync(RepoRef repo, CancellationToken ct)
    {
        if (IndexHasNoExtendedFlags(repo))
        {
            return;
        }

        var listed = await RunAsync(Invocation(repo, readOnly: true, "ls-files", "-z", "-t"), ct).ConfigureAwait(false);
        var marked = listed.SplitStandardOutputOnNul().Where(entry => entry.StartsWith("S ", StringComparison.Ordinal)).Select(entry => entry[2..]).ToArray();
        await SetSkipWorktreeAsync(repo, on: false, marked, ct).ConfigureAwait(false);
    }

    // git writes an index as version 3 only while an entry carries an extended flag (skip-worktree among them) and as version 2 otherwise,
    // so a version 2 header answers "no mark" without a git process. Anything unreadable gets the full question.
    private static bool IndexHasNoExtendedFlags(RepoRef repo)
    {
        try
        {
            Span<byte> header = stackalloc byte[8];
            using var stream = new FileStream(Path.Combine(repo.GitDir, "index"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return stream.ReadAtLeast(header, 8, throwOnEndOfStream: false) == 8
                && header[0] == (byte)'D' && header[1] == (byte)'I' && header[2] == (byte)'R' && header[3] == (byte)'C'
                && System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(header[4..]) == 2;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    // Which of these paths the tree holds as a file.
    private async Task<HashSet<string>> InTreeAsync(RepoRef repo, string tree, IReadOnlyCollection<string> paths, CancellationToken ct)
    {
        // A name with a line break cannot be asked about in the line format; it is left unprotected rather than guessed at.
        var asked = paths.Where(path => path.AsSpan().IndexOfAny('\n', '\r') < 0).ToArray();
        if (asked.Length == 0)
        {
            return [];
        }

        var result = await RunAsync(Invocation(repo, readOnly: true, "cat-file", "--batch-check") with { Stdin = string.Join('\n', asked.Select(path => $"{tree}:{path}")) + "\n" }, ct).ConfigureAwait(false);
        var lines = result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        return [.. asked.Where((_, index) => index < lines.Length && !lines[index].TrimEnd('\r').EndsWith(" missing", StringComparison.Ordinal))];
    }

    // Best effort for a path that is already failing: the error that got it here is the one worth reporting.
    private async Task TryClearSkipWorktreeAsync(RepoRef repo)
    {
        try
        {
            await ClearSkipWorktreeAsync(repo, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception e) when (e is SavepointException or IOException or UnauthorizedAccessException)
        {
        }
    }

    // git reads an index file that is not there as an empty one and lists nothing, which would read as "no file is named"; the answer is
    // only taken from an index that was there for the whole question.
    private static void RequireIndex(string index)
    {
        if (!File.Exists(index))
        {
            throw new GitOperationException($"The index '{index}' a classification works on disappeared, so its answer cannot be trusted.");
        }
    }

    // A recorded file the patterns name stays out of the next point: it leaves the index, and the file on disk stays where it is.
    private async Task DropNamedFromIndexAsync(RepoRef repo, CancellationToken ct)
    {
        var named = await RecordedNamedAsync(repo, ct).ConfigureAwait(false);
        if (named.Count > 0)
        {
            await RunWriteAsync(() => Invocation(repo, readOnly: false, "update-index", "--force-remove", "-z", "--stdin") with { Stdin = NulList(named) }, ct).ConfigureAwait(false);
        }
    }

    private static string NulList(IEnumerable<string> paths)
    {
        var list = new StringBuilder();
        foreach (var path in paths)
        {
            list.Append(path).Append('\0');
        }

        return list.ToString();
    }
}
