using System.Globalization;
using System.Text.RegularExpressions;
using Savepoint.Core.Errors;

namespace Savepoint.Core.Git;

public sealed partial class GitEngine
{
    /// <summary>How many objects are stored loose (not yet in a pack); 0 when the repository does not exist.</summary>
    /// <param name="repo">The folder.</param>
    /// <param name="ct">Cancels the call.</param>
    public async Task<int> CountLooseObjectsAsync(RepoRef repo, CancellationToken ct = default)
    {
        repo = Normalize(repo);
        return HasRepository(repo) ? (int)await ObjectStoreFigureAsync(repo, "count", ct).ConfigureAwait(false) : 0;
    }

    /// <summary>The bytes the object store takes on disk, loose objects and packs together; 0 when the repository does not exist.</summary>
    /// <param name="repo">The folder.</param>
    /// <param name="ct">Cancels the call.</param>
    public async Task<long> GetRepoSizeAsync(RepoRef repo, CancellationToken ct = default)
    {
        repo = Normalize(repo);
        if (!HasRepository(repo))
        {
            return 0;
        }

        var output = await CountObjectsAsync(repo, ct).ConfigureAwait(false);
        return (Figure(output, "size") + Figure(output, "size-pack")) * 1024;
    }

    /// <summary>
    /// Tidies the store. <see cref="OptimizationMode.Routine"/> packs the loose objects with <c>git repack -d --geometric=2</c>:
    /// new objects go into a new pack and only small packs are merged, so the large packs written earlier are left alone;
    /// objects no branch reaches any more (a deleted profile, points a move-forward dropped) are packed along, not removed.
    /// <see cref="OptimizationMode.Reclaim"/> expires reflogs, packs every reachable object into one pack and prunes the
    /// rest, so those objects are gone afterwards; it rewrites the whole store. Neither mode detaches or starts garbage
    /// collection, and neither needs the game folder. Nothing a branch, HEAD or the index reaches is ever dropped.
    /// </summary>
    /// <param name="repo">The folder whose history is optimized.</param>
    /// <param name="mode">How far to go.</param>
    /// <param name="ct">Cancels the call; every step is safe to interrupt and a repeat finishes the job.</param>
    /// <exception cref="Savepoint.Core.Errors.DiskSpaceException">The storage volume is below the free-space floor (a reclaim also needs room for a copy of the store); git was not started.</exception>
    /// <exception cref="Savepoint.Core.Errors.OperationCancelledByUserException">The token was cancelled.</exception>
    /// <exception cref="Savepoint.Core.Errors.GitOperationException">git failed, or a reclaim was asked for while this engine is writing to the same repository (it would prune objects that are about to be referenced).</exception>
    public async Task RepackAsync(RepoRef repo, OptimizationMode mode, CancellationToken ct = default)
    {
        repo = Normalize(repo);
        try
        {
            ct.ThrowIfCancellationRequested();
            switch (mode)
            {
                case OptimizationMode.Routine:
                    EnsureStorageSpace(repo);
                    using (BeginWrite(repo))
                    {
                        await RunWriteAsync(() => GitDirOnly(repo, "repack", "-q", "-d", "--geometric=2") with { ReadOnly = false }, ct).ConfigureAwait(false);
                    }

                    break;
                case OptimizationMode.Reclaim:
                    await ReclaimAsync(repo, ct).ConfigureAwait(false);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown optimization mode.");
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw new OperationCancelledByUserException(CancelledOperation.Compaction, repo.GameId);
        }
    }

    // Everything no branch, HEAD or the index reaches goes: reflogs first (they pin old points), then one pack of what is
    // reachable, then the loose objects nothing reaches.
    private async Task ReclaimAsync(RepoRef repo, CancellationToken ct)
    {
        // The new pack is written before the old ones are removed, so the store briefly takes twice its size.
        EnsureStorageSpace(repo, await GetRepoSizeAsync(repo, ct).ConfigureAwait(false));

        using var scope = BeginWrite(repo);

        // An object another operation of this engine has just written but not yet referenced would be pruned.
        if (!scope.IsAlone)
        {
            throw new GitOperationException(null, "Another operation is writing to this repository, so it cannot be reclaimed right now.");
        }

        await RunWriteAsync(() => GitDirOnly(repo, "reflog", "expire", "--expire=now", "--all") with { ReadOnly = false }, ct).ConfigureAwait(false);
        await RunWriteAsync(() => GitDirOnly(repo, "repack", "-q", "-a", "-d") with { ReadOnly = false }, ct).ConfigureAwait(false);
        await RunWriteAsync(() => GitDirOnly(repo, "prune", "--expire=now") with { ReadOnly = false }, ct).ConfigureAwait(false);
    }

    private async Task<long> ObjectStoreFigureAsync(RepoRef repo, string name, CancellationToken ct) =>
        Figure(await CountObjectsAsync(repo, ct).ConfigureAwait(false), name);

    private async Task<string> CountObjectsAsync(RepoRef repo, CancellationToken ct) =>
        (await RunAsync(GitDirOnly(repo, "count-objects", "-v"), ct).ConfigureAwait(false)).StandardOutput;

    // "count-objects -v" prints "name: value" lines; sizes are in KiB.
    private static long Figure(string output, string name) =>
        FigureLine().Matches(output).FirstOrDefault(match => match.Groups["name"].Value == name) is { } line
            ? long.Parse(line.Groups["value"].Value, CultureInfo.InvariantCulture)
            : 0;

    [GeneratedRegex(@"^(?<name>[a-z-]+):\s*(?<value>\d+)\s*$", RegexOptions.Multiline)]
    private static partial Regex FigureLine();
}
