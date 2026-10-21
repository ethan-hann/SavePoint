using Savepoint.Core.Errors;

namespace Savepoint.Core.Git;

public sealed partial class GitEngine
{
    /// <summary>
    /// The files a capture would record right now, with their sizes: everything git tracks plus every new file that is
    /// not ignored. Rewrites <c>info/exclude</c> from <paramref name="ignorePatterns"/> (in the storage folder; the work
    /// tree is never written). A file that disappears while its size is read is left out.
    /// </summary>
    /// <param name="repo">The folder.</param>
    /// <param name="ignorePatterns">The game's ignore patterns.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <exception cref="Savepoint.Core.Errors.PathSafetyException">The work tree is a protected location.</exception>
    /// <exception cref="Savepoint.Core.Errors.ConsentMarkerException">The folder has no marker for this game and role.</exception>
    public async Task<IReadOnlyList<CaptureCandidate>> ListCaptureCandidatesAsync(RepoRef repo, IReadOnlyList<string> ignorePatterns, CancellationToken ct = default)
    {
        repo = PreparePreview(repo, ignorePatterns, ct);

        var listing = await RunAsync(
            Invocation(repo, readOnly: true, "ls-files", "-z", "--cached", "--others", "--exclude-standard", "--deduplicate"),
            ct).ConfigureAwait(false);
        var paths = listing.SplitStandardOutputOnNul();

        // Each size is a file-system call; a tree of tens of thousands of files is measured in parallel.
        var sizes = new long[paths.Count];
        await Task.Run(
            () => Parallel.For(0, paths.Count, new ParallelOptions { CancellationToken = ct, MaxDegreeOfParallelism = Environment.ProcessorCount }, i => sizes[i] = SizeOrMissing(repo, paths[i])),
            ct).ConfigureAwait(false);

        var candidates = new List<CaptureCandidate>(paths.Count);
        for (var i = 0; i < paths.Count; i++)
        {
            if (sizes[i] >= 0)
            {
                candidates.Add(new CaptureCandidate(paths[i], sizes[i]));
            }
        }

        return candidates;
    }

    /// <summary>
    /// How many of the candidates' bytes a capture would add to the store: files whose content it already holds, under
    /// any name or on any branch, cost nothing. Files git can show are unchanged since the last point are not read.
    /// A file that cannot be read because another program holds it is counted in full.
    /// </summary>
    /// <param name="repo">The folder.</param>
    /// <param name="candidates">From <see cref="ListCaptureCandidatesAsync"/>.</param>
    /// <param name="ignorePatterns">The same ignore patterns the candidates were listed with.</param>
    /// <param name="ct">Cancels the call.</param>
    public async Task<long> EstimateNewBytesAsync(RepoRef repo, IReadOnlyList<CaptureCandidate> candidates, IReadOnlyList<string> ignorePatterns, CancellationToken ct = default)
    {
        repo = PreparePreview(repo, ignorePatterns, ct);
        ArgumentNullException.ThrowIfNull(candidates);
        if (candidates.Count == 0)
        {
            return 0;
        }

        // Nothing is stored yet, so everything is new and there is nothing to compare with.
        var head = GitDirFiles.ReadHead(repo.GitDir);
        var tip = head.IsDetached ? head.DetachedCommit : await ResolveBranchAsync(repo, head.Branch!, ct).ConfigureAwait(false);
        if (tip is null)
        {
            return candidates.Sum(candidate => candidate.Bytes);
        }

        var needCheck = await FilesGitCannotVouchForAsync(repo, ct).ConfigureAwait(false);
        var toCheck = candidates.Where(candidate => needCheck.Contains(candidate.Path)).ToArray();
        if (toCheck.Length == 0)
        {
            return 0;
        }

        var outcomes = new HashOutcome[toCheck.Length];
        var ids = new string[toCheck.Length];
        await Task.Run(
            () => Parallel.For(0, toCheck.Length, new ParallelOptions { CancellationToken = ct, MaxDegreeOfParallelism = Environment.ProcessorCount }, i =>
                outcomes[i] = BlobHasher.TryHash(FullPath(repo, toCheck[i].Path), out ids[i])),
            ct).ConfigureAwait(false);

        var hashed = Enumerable.Range(0, toCheck.Length).Where(i => outcomes[i] == HashOutcome.Hashed).ToArray();
        var missing = await MissingObjectsAsync(repo, hashed.Select(i => ids[i]).Distinct(StringComparer.Ordinal), ct).ConfigureAwait(false);

        // The same new content under two names is stored once, so it is counted once.
        var counted = new HashSet<string>(StringComparer.Ordinal);
        var bytes = 0L;
        foreach (var i in hashed.Where(i => missing.Contains(ids[i]) && counted.Add(ids[i])))
        {
            bytes += toCheck[i].Bytes;
        }

        // A file that cannot be read cannot be compared; counting it keeps the estimate on the safe side.
        return bytes + Enumerable.Range(0, toCheck.Length).Where(i => outcomes[i] == HashOutcome.Unreadable).Sum(i => toCheck[i].Bytes);
    }

    /// <summary>
    /// What switching from <paramref name="fromRevision"/> to <paramref name="toRevision"/> would change. Reads only the git directory.
    /// </summary>
    /// <param name="repo">The folder.</param>
    /// <param name="fromRevision">A full commit id or a branch name.</param>
    /// <param name="toRevision">A full commit id or a branch name.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <exception cref="Savepoint.Core.Errors.GitOperationException">A revision does not exist.</exception>
    public async Task<DiffSummary> DiffStatAsync(RepoRef repo, string fromRevision, string toRevision, CancellationToken ct = default)
    {
        repo = Normalize(repo);
        var from = await TryResolveRevisionAsync(repo, fromRevision, ct).ConfigureAwait(false)
            ?? throw new GitOperationException(null, $"'{fromRevision}' is not a point in the repository.");
        var to = await TryResolveRevisionAsync(repo, toRevision, ct).ConfigureAwait(false)
            ?? throw new GitOperationException(null, $"'{toRevision}' is not a point in the repository.");

        var changes = await DiffTreesAsync(repo, from, to, ct).ConfigureAwait(false);
        var sizes = await ObjectSizesAsync(repo, changes.Select(change => change.NewId), ct).ConfigureAwait(false);
        return new DiffSummary(changes.Count, changes.Sum(change => sizes.GetValueOrDefault(change.NewId)), [.. changes.Select(change => change.Path)]);
    }

    /// <summary>The files in a point's tree, relative paths with forward slashes; empty when the revision does not exist.</summary>
    /// <param name="repo">The folder.</param>
    /// <param name="revision">A full commit id or a branch name.</param>
    /// <param name="ct">Cancels the call.</param>
    public async Task<IReadOnlyList<string>> ListTreeFilesAsync(RepoRef repo, string revision, CancellationToken ct = default)
    {
        repo = Normalize(repo);
        if (await TryResolveRevisionAsync(repo, revision, ct).ConfigureAwait(false) is not { } commit)
        {
            return [];
        }

        var result = await RunAsync(GitDirOnly(repo, "ls-tree", "-r", "-z", "--name-only", commit), ct).ConfigureAwait(false);
        return result.SplitStandardOutputOnNul();
    }

    /// <summary>The exact bytes a point holds for a file, or null when the point has no such file. Binary safe.</summary>
    /// <param name="repo">The folder.</param>
    /// <param name="revision">A full commit id or a branch name.</param>
    /// <param name="path">Relative to the work tree; backslashes are read as slashes.</param>
    /// <param name="ct">Cancels the call.</param>
    public async Task<byte[]?> ReadBlobAsync(RepoRef repo, string revision, string path, CancellationToken ct = default)
    {
        if (await GetBlobIdAsync(repo, revision, path, ct).ConfigureAwait(false) is not { } id)
        {
            return null;
        }

        var result = await RunAsync(GitDirOnly(repo, "cat-file", "blob", id) with { BinaryStdout = true }, ct).ConfigureAwait(false);
        return result.StandardOutputBytes;
    }

    /// <summary>The id of a file's content at a point (the same id for the same bytes anywhere), or null when there is no such file. A cache can key on it.</summary>
    /// <param name="repo">The folder.</param>
    /// <param name="revision">A full commit id or a branch name.</param>
    /// <param name="path">Relative to the work tree; backslashes are read as slashes.</param>
    /// <param name="ct">Cancels the call.</param>
    public async Task<string?> GetBlobIdAsync(RepoRef repo, string revision, string path, CancellationToken ct = default)
    {
        repo = Normalize(repo);
        ArgumentNullException.ThrowIfNull(path);
        var normalized = path.Replace('\\', '/').Trim('/');

        // The spec goes to git as one line, so a name with a line break cannot be asked about (and cannot exist on Windows).
        if (normalized.Length == 0 || normalized.AsSpan().IndexOfAny('\n', '\r') >= 0
            || await TryResolveRevisionAsync(repo, revision, ct).ConfigureAwait(false) is not { } commit)
        {
            return null;
        }

        var result = await RunAsync(GitDirOnly(repo, "cat-file", "--batch-check") with { Stdin = $"{commit}:{normalized}\n" }, ct).ConfigureAwait(false);
        var fields = result.StandardOutput.Trim().Split(' ');

        // "<id> blob <size>" for a file; a folder is a tree, and a path that is not there is reported as missing.
        return fields.Length == 3 && fields[1] == "blob" ? fields[0] : null;
    }

    // Both previews read the work tree, so both need it to be a place SavePoint may look at; they also make the ignore rules current.
    private RepoRef PreparePreview(RepoRef repo, IReadOnlyList<string> ignorePatterns, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(ignorePatterns);
        repo = Normalize(repo);
        ct.ThrowIfCancellationRequested();
        CheckWorkTree(repo);
        RepoMarker.Verify(repo);
        if (!IsInitialized(repo.GitDir))
        {
            throw new GitOperationException(null, $"The repository at '{repo.GitDir}' does not exist.");
        }

        RepoIgnoreFiles.WriteExclude(repo.GitDir, ignorePatterns);
        return repo;
    }

    private static long SizeOrMissing(RepoRef repo, string path)
    {
        try
        {
            var info = new FileInfo(FullPath(repo, path));
            return info.Exists ? info.Length : -1;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Removed or locked between the listing and now: it is not part of what can be captured.
            return -1;
        }
    }

    // What differs from the last point, plus what is new: every other tracked file has the stat data it had when it was recorded, so git already holds its content.
    private async Task<HashSet<string>> FilesGitCannotVouchForAsync(RepoRef repo, CancellationToken ct)
    {
        var changed = RunAsync(Invocation(repo, readOnly: true, "diff", "--name-only", "-z", "--no-renames", "HEAD", "--"), ct);
        var untracked = RunAsync(Invocation(repo, readOnly: true, "ls-files", "-z", "--others", "--exclude-standard"), ct);

        var names = new HashSet<string>(StringComparer.Ordinal);
        names.UnionWith((await changed.ConfigureAwait(false)).SplitStandardOutputOnNul());
        names.UnionWith((await untracked.ConfigureAwait(false)).SplitStandardOutputOnNul());
        return names;
    }

    private async Task<HashSet<string>> MissingObjectsAsync(RepoRef repo, IEnumerable<string> ids, CancellationToken ct)
    {
        var wanted = ids.ToArray();
        var missing = new HashSet<string>(StringComparer.Ordinal);
        if (wanted.Length == 0)
        {
            return missing;
        }

        var result = await RunAsync(GitDirOnly(repo, "cat-file", "--batch-check") with { Stdin = string.Join('\n', wanted) + "\n" }, ct).ConfigureAwait(false);
        foreach (var line in result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = line.TrimEnd('\r').Split(' ');
            if (fields.Length == 2 && fields[1] == "missing")
            {
                missing.Add(fields[0]);
            }
        }

        return missing;
    }

    // A full commit id that exists, or the tip of the branch of that name; null when neither does.
    private async Task<string?> TryResolveRevisionAsync(RepoRef repo, string revision, CancellationToken ct)
    {
        if (revision is null || !HasRepository(repo))
        {
            return null;
        }

        if (GitDirFiles.IsObjectId(revision))
        {
            return await ResolveCommitAsync(repo, revision, ct).ConfigureAwait(false);
        }

        return BranchNames.IsValid(revision) ? await ResolveBranchAsync(repo, revision, ct).ConfigureAwait(false) : null;
    }
}
