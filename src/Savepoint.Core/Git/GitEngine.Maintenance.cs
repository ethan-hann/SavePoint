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
    /// Packs the loose objects with <c>git repack -d --geometric=2</c>: new objects go into a new pack and only small
    /// packs are merged, so the large packs written earlier are left alone. Never detaches and never starts garbage
    /// collection. Needs only the storage volume, not the game folder. Objects no branch reaches any more (a deleted
    /// profile, points a move-forward dropped) are packed along with the rest, not removed.
    /// </summary>
    /// <param name="repo">The folder whose history is optimized.</param>
    /// <param name="ct">Cancels the call; repacking is safe to interrupt.</param>
    /// <exception cref="Savepoint.Core.Errors.DiskSpaceException">The storage volume is below the free-space floor; git was not started.</exception>
    /// <exception cref="Savepoint.Core.Errors.OperationCancelledByUserException">The token was cancelled.</exception>
    /// <exception cref="Savepoint.Core.Errors.GitOperationException">git failed.</exception>
    public async Task RepackAsync(RepoRef repo, CancellationToken ct = default)
    {
        repo = Normalize(repo);
        try
        {
            ct.ThrowIfCancellationRequested();
            EnsureStorageSpace(repo);

            using var scope = BeginWrite(repo);
            await RunWriteAsync(() => GitDirOnly(repo, "repack", "-q", "-d", "--geometric=2") with { ReadOnly = false }, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw new OperationCancelledByUserException(CancelledOperation.Compaction, repo.GameId);
        }
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
