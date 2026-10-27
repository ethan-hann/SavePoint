namespace Savepoint.Core.Git;

public sealed partial class GitEngine
{
    // The bytes a save of these files would add to the store. git keeps one copy of any content, so a file whose content is already
    // stored (put back as an older snapshot held it, or copied under a new name) adds nothing. Telling that takes a read, so only the
    // smallest files are read, up to a fixed total; everything else, and every file that cannot be read or is not ordinary content, counts
    // in full. The estimate can therefore only be too high, except for a file that changes between this read and the add, or one a
    // conversion rule inside the game folder rewrites on the way in: both are limited to the bytes read here, which stays below the
    // free-space floor that is kept on top.
    private async Task<long> BytesNotAlreadyStoredAsync(RepoRef repo, IReadOnlyList<string> paths, long[] sizes, CancellationToken ct)
    {
        var total = sizes.Sum();
        var budget = _options.StoredContentReadBytes;
        if (total == 0 || budget <= 0 || !HasSnapshot(repo))
        {
            return total;
        }

        // A file git streams into a pack is written out before git finds it is already stored, so it needs room whatever it holds.
        var streamedFrom = _options.StagingLimits.BigBytes;
        var chosen = new List<int>();
        var spent = 0L;
        foreach (var i in Enumerable.Range(0, paths.Count).Where(i => sizes[i] > 0 && sizes[i] < streamedFrom).OrderBy(i => sizes[i]))
        {
            if (spent + sizes[i] > budget)
            {
                break;
            }

            chosen.Add(i);
            spent += sizes[i];
        }

        var ids = new string?[chosen.Count];
        await Task.Run(
            () => Parallel.For(0, chosen.Count, new ParallelOptions { CancellationToken = ct, MaxDegreeOfParallelism = Environment.ProcessorCount }, n =>
            {
                var full = FullPath(repo, paths[chosen[n]]);

                // A link is stored as the text of its target, not as the file it points at.
                if ((new FileInfo(full).Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    return;
                }

                _options.FileHashed?.Invoke(full);
                if (BlobHasher.TryHash(full, out var id) == HashOutcome.Hashed)
                {
                    ids[n] = id;
                }
            }),
            ct).ConfigureAwait(false);

        var missing = await MissingObjectsAsync(repo, ids.OfType<string>().Distinct(StringComparer.Ordinal), ct).ConfigureAwait(false);
        var stored = 0L;
        for (var n = 0; n < chosen.Count; n++)
        {
            if (ids[n] is { } id && !missing.Contains(id))
            {
                stored += sizes[chosen[n]];
            }
        }

        return total - stored;
    }

    // Whether HEAD can name a snapshot (a detached one, a branch with a ref file, or any packed ref); with none, nothing is stored yet and
    // reading files to look for stored content would be wasted. A wrong "yes" only costs the reads.
    private static bool HasSnapshot(RepoRef repo)
    {
        var head = GitDirFiles.ReadHead(repo.GitDir);
        if (head.IsDetached)
        {
            return head.DetachedCommit is not null;
        }

        return GitDirFiles.ReadLooseRef(repo.GitDir, GitDirFiles.BranchPrefix + head.Branch!) is not null
            || File.Exists(Path.Combine(repo.GitDir, "packed-refs"));
    }
}
