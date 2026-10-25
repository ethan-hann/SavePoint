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

    /// <summary>The bytes the object store takes on disk: loose objects, packs and the leftovers git counts as garbage (a temporary pack a killed git left behind); 0 when the repository does not exist.</summary>
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
        return (Figure(output, "size") + Figure(output, "size-pack") + Figure(output, "size-garbage")) * 1024;
    }

    /// <summary>
    /// Tidies the store. <see cref="OptimizationMode.Routine"/> packs the loose objects with <c>git repack -d --geometric=2</c>:
    /// new objects go into a new pack and only small packs are merged, so the large packs written earlier are left alone (a pack a capture streamed its big files into carries a <c>.keep</c> file, and a pack as large as the big-file size gets one here);
    /// objects no branch reaches any more (a deleted profile, points a move-forward dropped) are packed along, not removed.
    /// <see cref="OptimizationMode.Reclaim"/> expires reflogs, removes SavePoint's <c>.keep</c> files, packs every reachable object into one pack and prunes the
    /// rest, so those objects are gone afterwards; it rewrites the whole store. Neither mode deletes a record of a removed snapshot
    /// (see <see cref="ListRemovedAsync(RepoRef, IReadOnlyCollection{string}, CancellationToken)"/>); only <see cref="ClearRemovedAsync"/> does, so what a record reaches survives both. No mode
    /// detaches or starts garbage collection, and none needs the game folder. Nothing a branch, HEAD, the index or a record reaches is
    /// ever dropped.
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
                        KeepLargePacks(repo);
                        await RunWriteAsync(() => GitDirOnly(repo, "repack", "-q", "-d", "--geometric=2") with { ReadOnly = false, ExtraEnvironment = _defaultPackCompression }, ct).ConfigureAwait(false);
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

    /// <summary>
    /// Runs every check that can make a <see cref="OptimizationMode.Reclaim"/> refuse, and changes nothing: the storage volume has room for
    /// a copy of the store, and no other operation of this engine is writing to the repository. A caller that is about to delete records
    /// before reclaiming asks first, so a refusal leaves them alone.
    /// </summary>
    /// <param name="repo">The folder whose history would be reclaimed.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <exception cref="Savepoint.Core.Errors.DiskSpaceException">The storage volume is below the free-space floor plus the store's size.</exception>
    /// <exception cref="Savepoint.Core.Errors.OperationCancelledByUserException">The token was cancelled.</exception>
    /// <exception cref="Savepoint.Core.Errors.GitOperationException">Another operation of this engine is writing to the repository.</exception>
    public async Task CheckReclaimAsync(RepoRef repo, CancellationToken ct = default)
    {
        repo = Normalize(repo);
        try
        {
            ct.ThrowIfCancellationRequested();
            EnsureStorageSpace(repo, await GetRepoSizeAsync(repo, ct).ConfigureAwait(false));
            using var scope = BeginWrite(repo);
            RequireAlone(scope);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw new OperationCancelledByUserException(CancelledOperation.Compaction, repo.GameId);
        }
    }

    // A reclaim notes itself in the git directory (see ReclaimMarker). When the SavePoint that ran it is gone and the git it started last is
    // still running, that git goes on deleting packs, so anything written meanwhile could lose the only copy of an object that
    // sat in a pack being replaced. This waits for that git to end, then removes the note. A note whose owner still runs is a reclaim in progress.
    // onWaiting is called just before the wait begins, so a screen can say why nothing moves.
    internal async Task SettleReclaimAsync(string gitDir, CancellationToken ct, Action? onWaiting = null)
    {
        if (!File.Exists(ReclaimMarker.PathIn(gitDir)))
        {
            return;
        }

        if (ReclaimMarker.Read(gitDir) is { } note)
        {
            if (note.Owner.IsRunning())
            {
                return;
            }

            if (note.Git is { } git && git.IsRunning())
            {
                onWaiting?.Invoke();
                await git.WaitForExitAsync(ct).ConfigureAwait(false);
            }
        }

        ReclaimMarker.Clear(gitDir);
    }

    // An object another operation of this engine has just written but not yet referenced would be pruned.
    private static void RequireAlone(InFlightTable.Scope scope)
    {
        if (!scope.IsAlone)
        {
            throw new GitOperationException("Another operation is writing to this repository, so it cannot be reclaimed right now.");
        }
    }

    // Everything no branch, HEAD, the index or a removed-snapshot record reaches goes: reflogs first (they pin old points), then
    // one pack of what is reachable, then the loose objects nothing reaches.
    private async Task ReclaimAsync(RepoRef repo, CancellationToken ct)
    {
        // The new pack is written before the old ones are removed, so the store briefly takes twice its size.
        EnsureStorageSpace(repo, await GetRepoSizeAsync(repo, ct).ConfigureAwait(false));

        using var scope = BeginWrite(repo);
        RequireAlone(scope);

        // Noted before git starts and again for each git that does, so a SavePoint that is killed in the middle leaves the next start the process to wait for.
        ReclaimMarker.Begin(repo.GitDir);
        try
        {
            // A repack that packs everything leaves kept packs alone, so a big file nothing reaches would stay; the keeps go first.
            RemoveKeeps(repo);
            await RunWriteAsync(() => ReclaimStep(repo, "reflog", "expire", "--expire=now", "--all"), ct).ConfigureAwait(false);
            await RunWriteAsync(() => ReclaimStep(repo, "repack", "-q", "-a", "-d") with { ExtraEnvironment = _defaultPackCompression }, ct).ConfigureAwait(false);
            await RunWriteAsync(() => ReclaimStep(repo, "prune", "--expire=now"), ct).ConfigureAwait(false);
        }
        finally
        {
            ReclaimMarker.Clear(repo.GitDir);
        }
    }

    // A geometric repack weighs packs by object count, so the pack a capture streams its big files into (one or a few objects) always looks
    // lightest and would be rolled up and copied. A ".keep" beside a pack makes repack leave it alone. Only SavePoint's own keeps carry
    // this text, so a reclaim never removes one it did not write.
    private const string _streamedPackKeepText = "SavePoint: holds files streamed at capture; routine repacks leave it alone, Free up space merges it.\n";

    private static string PackFolderOf(RepoRef repo) => Path.Combine(repo.GitDir, "objects", "pack");

    private static HashSet<string> PackFilesOf(RepoRef repo) =>
        Directory.Exists(PackFolderOf(repo))
            ? [.. Directory.EnumerateFiles(PackFolderOf(repo), "*.pack")]
            : [];

    private static void Keep(string pack)
    {
        var keep = Path.ChangeExtension(pack, ".keep");
        if (!File.Exists(keep))
        {
            File.WriteAllText(keep, _streamedPackKeepText);
        }
    }

    // Packs that appeared while an add ran hold what it streamed; small files stay loose until a repack.
    private static void KeepPacksSince(RepoRef repo, HashSet<string> before)
    {
        foreach (var pack in PackFilesOf(repo).Where(pack => !before.Contains(pack)))
        {
            Keep(pack);
        }
    }

    // Stores made before captures marked their packs have no keeps. A pack as large as the big-file size holds a streamed file or
    // is already heavier than anything a routine repack should copy, so it is protected the same way.
    private void KeepLargePacks(RepoRef repo)
    {
        var big = _options.StagingLimits.BigBytes;
        foreach (var pack in PackFilesOf(repo).Where(pack => new FileInfo(pack).Length >= big))
        {
            Keep(pack);
        }
    }

    private static void RemoveKeeps(RepoRef repo)
    {
        if (!Directory.Exists(PackFolderOf(repo)))
        {
            return;
        }

        foreach (var keep in Directory.EnumerateFiles(PackFolderOf(repo), "*.keep"))
        {
            if (File.ReadAllText(keep) == _streamedPackKeepText)
            {
                File.Delete(keep);
            }
        }
    }

    // The repository sets pack.compression = 0 for the big files a capture streams. A repack compresses what it packs anew at zlib's
    // fastest level and copies data already in a pack as it is, so those big files stay as captured. The default level cost most of
    // a repack on game archives that shrink by under 6%, while level 1 still shrinks saves and configs. Environment config keeps
    // the argument list as it is and outranks the repository file.
    private static readonly IReadOnlyDictionary<string, string?> _defaultPackCompression = new Dictionary<string, string?>
    {
        ["GIT_CONFIG_COUNT"] = (GitBinary.ForcedConfigCount + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
        ["GIT_CONFIG_KEY_" + GitBinary.ForcedConfigCount.ToString(System.Globalization.CultureInfo.InvariantCulture)] = "pack.compression",
        ["GIT_CONFIG_VALUE_" + GitBinary.ForcedConfigCount.ToString(System.Globalization.CultureInfo.InvariantCulture)] = "1",
    };

    private static GitInvocation ReclaimStep(RepoRef repo, params string[] arguments) =>
        GitDirOnly(repo, arguments) with { ReadOnly = false, Started = git => ReclaimMarker.Record(repo.GitDir, git) };

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
