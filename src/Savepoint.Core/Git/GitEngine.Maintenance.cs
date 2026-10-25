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
    /// <see cref="OptimizationMode.Reclaim"/> expires reflogs, removes the <c>.keep</c> of every SavePoint-kept pack that holds an object nothing reaches, packs every reachable object
    /// outside the packs that stay kept into one pack and prunes the rest, so the unreachable objects are gone afterwards. It rewrites only what it frees: a kept pack whose
    /// objects are all reachable is left byte for byte as it is, and when nothing is unreachable or loose it starts no repack. Neither mode deletes a record of a removed snapshot
    /// (see <see cref="ListRemovedAsync(RepoRef, IReadOnlyCollection{string}, CancellationToken)"/>); only <see cref="ClearRemovedAsync"/> does, so what a record reaches survives both. No mode
    /// detaches or starts garbage collection, and none needs the game folder. Nothing a branch, HEAD, the index or a record reaches is
    /// ever dropped.
    /// </summary>
    /// <param name="repo">The folder whose history is optimized.</param>
    /// <param name="mode">How far to go.</param>
    /// <param name="ct">Cancels the call; every step is safe to interrupt and a repeat finishes the job.</param>
    /// <exception cref="Savepoint.Core.Errors.DiskSpaceException">The storage volume is below the free-space floor (a reclaim also needs room for a copy of the store); git was not started.</exception>
    /// <exception cref="Savepoint.Core.Errors.OperationCancelledByUserException">The token was cancelled.</exception>
    /// <exception cref="Savepoint.Core.Errors.GitOperationException">git failed, a pack's <c>.keep</c> file could not be written (routine) or removed (reclaim), or a reclaim was asked for while this engine is writing to the same repository (it would prune objects that are about to be referenced).</exception>
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
    /// a copy of what the reclaim would rewrite (the loose objects, and the packs that hold something nothing reaches; a pack whose objects
    /// are all reachable is never copied), and no other operation of this engine is writing to the repository. A caller that is about to delete records
    /// before reclaiming asks first, so a refusal leaves them alone.
    /// </summary>
    /// <param name="repo">The folder whose history would be reclaimed.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <exception cref="Savepoint.Core.Errors.DiskSpaceException">The storage volume is below the free-space floor plus the bytes to rewrite.</exception>
    /// <exception cref="Savepoint.Core.Errors.OperationCancelledByUserException">The token was cancelled.</exception>
    /// <exception cref="Savepoint.Core.Errors.GitOperationException">Another operation of this engine is writing to the repository.</exception>
    public Task CheckReclaimAsync(RepoRef repo, CancellationToken ct = default) => CheckReclaimAsync(repo, [], ct);

    /// <param name="repo">The folder whose history would be reclaimed.</param>
    /// <summary>The same check, counting the refs the caller is about to delete (removed-snapshot records, stray branches) as already gone.</summary>
    /// <param name="clearing">The full names of those refs.</param>
    /// <param name="ct">Cancels the call.</param>
    public async Task CheckReclaimAsync(RepoRef repo, IReadOnlyCollection<string> clearing, CancellationToken ct = default)
    {
        repo = Normalize(repo);
        try
        {
            ct.ThrowIfCancellationRequested();
            using var scope = BeginWrite(repo);
            RequireAlone(scope);

            // Counted as if the named refs were already gone, which can only make the figure larger than the run will need.
            var plan = await PlanReclaimAsync(repo, clearing, ct).ConfigureAwait(false);
            EnsureStorageSpace(repo, plan.RewriteBytes);
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
    // a pack of what is reachable (none when nothing is unreachable or loose), then the loose objects nothing reaches.
    private async Task ReclaimAsync(RepoRef repo, CancellationToken ct)
    {
        using var scope = BeginWrite(repo);
        RequireAlone(scope);

        // The new pack is written before the old ones are removed, so the store briefly takes the size of what it rewrites twice.
        // Reflogs do not change what is reachable, so the plan can be made before they are expired.
        var plan = await PlanReclaimAsync(repo, [], ct).ConfigureAwait(false);
        EnsureStorageSpace(repo, plan.RewriteBytes);

        // Noted before git starts and again for each git that does, so a SavePoint that is killed in the middle leaves the next start the process to wait for.
        ReclaimMarker.Begin(repo.GitDir);
        try
        {
            await RunWriteAsync(() => ReclaimStep(repo, "reflog", "expire", "--expire=now", "--all"), ct).ConfigureAwait(false);

            // A repack that packs everything leaves kept packs alone, so a kept pack holding an object nothing reaches loses its keep
            // and is rewritten; every other kept pack stays as it is.
            DeleteKeeps(plan.KeepsToDrop);
            if (plan.Repack != ReclaimRepack.None)
            {
                var arguments = plan.Repack == ReclaimRepack.Everything ? new[] { "repack", "-q", "-a", "-d" } : ["repack", "-q", "-d"];
                await RunWriteAsync(() => ReclaimStep(repo, arguments) with { ExtraEnvironment = _defaultPackCompression }, ct).ConfigureAwait(false);
            }

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
    private const string _keepTextStart = "SavePoint:";
    private const string _streamedPackKeepText = _keepTextStart + " holds files streamed at capture; repacks leave it alone, and Free up space rewrites it only when something in it is no longer needed.\n";

    private static string PackFolderOf(RepoRef repo) => Path.Combine(repo.GitDir, "objects", "pack");

    private static HashSet<string> PackFilesOf(RepoRef repo) =>
        Directory.Exists(PackFolderOf(repo))
            ? [.. Directory.EnumerateFiles(PackFolderOf(repo), "*.pack")]
            : [];

    private void Keep(string pack)
    {
        var keep = Path.ChangeExtension(pack, ".keep");
        if (!File.Exists(keep))
        {
            (_options.WriteKeep ?? (path => AtomicText.Write(path, _streamedPackKeepText)))(keep);
        }
    }

    // Packs that appeared while an add ran hold what it streamed; small files stay loose until a repack.
    // Best effort: this runs while a capture ends, and a failure here must not hide how the capture itself ended. A pack that misses its
    // keep is caught by the size sweep at the next routine repack.
    private void KeepPacksSince(RepoRef repo, HashSet<string> before)
    {
        try
        {
            foreach (var pack in PackFilesOf(repo).Where(pack => !before.Contains(pack)))
            {
                Keep(pack);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
        }
    }

    // A streamed pack holds only the files over the big-file size, a few to a few hundred in a real install (a Unity game has about 300
    // bundles). A pack of routine-merged saves and configs that grew past that size holds thousands of objects, so it stays in geometric
    // repacking.
    private const int _streamedPackMaxObjects = 1024;

    // Stores made before captures marked their packs have no keeps. A pack at least as large as the big-file size with few objects
    // holds streamed files, so it is protected the same way. A keep that cannot be written is reported like any other failure of the repack.
    private void KeepLargePacks(RepoRef repo)
    {
        var big = _options.StagingLimits.BigBytes;
        try
        {
            foreach (var pack in PackFilesOf(repo).Where(pack => new FileInfo(pack).Length >= big && ObjectCountOf(pack) is > 0 and <= _streamedPackMaxObjects))
            {
                Keep(pack);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new GitOperationException("A pack could not be marked to stay out of the repack: " + e.Message);
        }
    }

    // The index (version 2) opens with a magic number, a version and 256 cumulative counts; the last count is the number of objects.
    // Reading it costs no git process. 0 means the index could not be understood, which the caller treats as not streamed.
    private static long ObjectCountOf(string pack)
    {
        try
        {
            using var index = new FileStream(Path.ChangeExtension(pack, ".idx"), FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            Span<byte> header = stackalloc byte[8];
            if (index.Read(header) != 8 || !header.SequenceEqual((ReadOnlySpan<byte>)[0xff, 0x74, 0x4f, 0x63, 0, 0, 0, 2]))
            {
                return 0;
            }

            index.Seek(8 + (255 * 4), SeekOrigin.Begin);
            Span<byte> last = stackalloc byte[4];
            return index.Read(last) == 4 ? System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(last) : 0;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    private enum ReclaimRepack
    {
        /// <summary>Nothing to pack or drop.</summary>
        None,

        /// <summary>Only the loose objects go into a new pack; no existing pack is touched.</summary>
        LooseOnly,

        /// <summary>Every pack without a keep is rewritten, which is what drops an unreachable packed object.</summary>
        Everything,
    }

    private sealed record ReclaimPlan(ReclaimRepack Repack, IReadOnlyList<string> KeepsToDrop, long RewriteBytes);

    // Works out what a reclaim must rewrite, from the objects alone (no pack is read): the objects no ref, index entry or removed-snapshot
    // record reaches, which packs hold them, and the loose objects. A kept pack is rewritten only when it holds an unreachable object,
    // an unkept pack only when a repack of everything is needed anyway. Kept packs hold few objects and an index is small, so naming
    // each pack's objects costs little. "clearing" names refs to count as already deleted.
    private async Task<ReclaimPlan> PlanReclaimAsync(RepoRef repo, IReadOnlyCollection<string> clearing, CancellationToken ct)
    {
        var unreachable = await ObjectNamesAsync(repo, ["cat-file", "--batch-all-objects", "--unordered", "--batch-check=%(objectname)"], ct).ConfigureAwait(false);
        unreachable.ExceptWith(await ReachableObjectsAsync(repo, clearing, ct).ConfigureAwait(false));

        var figures = await CountObjectsAsync(repo, ct).ConfigureAwait(false);
        var looseCount = Figure(figures, "count");
        var looseBytes = Figure(figures, "size") * 1024;

        var drop = new List<string>();
        long packBytes = 0;
        var everything = false;
        if (unreachable.Count > 0)
        {
            var own = OwnKeepsOf(repo).ToHashSet(StringComparer.OrdinalIgnoreCase);
            foreach (var pack in PackFilesOf(repo))
            {
                var keep = Path.ChangeExtension(pack, ".keep");
                if (File.Exists(keep) && !own.Contains(keep))
                {
                    continue;
                }

                var holds = await HoldsAnyAsync(repo, pack, unreachable, ct).ConfigureAwait(false);
                if (File.Exists(keep))
                {
                    if (holds)
                    {
                        drop.Add(keep);
                        everything = true;
                        packBytes += SizeOfPack(pack);
                    }

                    continue;
                }

                everything |= holds;
                packBytes += SizeOfPack(pack);
            }

            drop.AddRange(own.Where(keep => !File.Exists(Path.ChangeExtension(keep, ".pack"))));
        }

        // The unkept packs were counted while looking; they only matter when they are rewritten.
        if (everything)
        {
            return new ReclaimPlan(ReclaimRepack.Everything, drop, packBytes + looseBytes);
        }

        return looseCount > 0 ? new ReclaimPlan(ReclaimRepack.LooseOnly, drop, looseBytes) : new ReclaimPlan(ReclaimRepack.None, drop, 0);
    }

    private static long SizeOfPack(string pack) =>
        new FileInfo(pack).Length + (File.Exists(Path.ChangeExtension(pack, ".idx")) ? new FileInfo(Path.ChangeExtension(pack, ".idx")).Length : 0);

    // --all covers every ref (so the removed-snapshot records) and HEAD. With refs to leave out, the rest are named one by one.
    private async Task<HashSet<string>> ReachableObjectsAsync(RepoRef repo, IReadOnlyCollection<string> without, CancellationToken ct)
    {
        if (without.Count == 0)
        {
            return await ObjectNamesAsync(repo, ["rev-list", "--objects", "--all", "--indexed-objects"], ct).ConfigureAwait(false);
        }

        var roots = (await RunAsync(GitDirOnly(repo, "for-each-ref", "--format=%(refname)"), ct).ConfigureAwait(false)).StandardOutput
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(name => !without.Contains(name))
            .ToList();
        if ((await RunAsync(GitDirOnly(repo, "rev-parse", "--verify", "-q", "HEAD") with { AllowedExitCodes = [0, 1] }, ct).ConfigureAwait(false)).ExitCode == 0)
        {
            roots.Add("HEAD");
        }

        return await ObjectNamesAsync(repo, ["rev-list", "--objects", "--indexed-objects", "--stdin"], ct, string.Concat(roots.Select(root => root + "\n"))).ConfigureAwait(false);
    }

    private static void DeleteKeeps(IReadOnlyList<string> keeps)
    {
        foreach (var keep in keeps)
        {
            try
            {
                File.Delete(keep);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                throw new GitOperationException("A pack's keep file could not be removed before the reclaim: " + e.Message);
            }
        }
    }

    // The first word of each output line, which is the object name for these commands (rev-list adds a path after it).
    private async Task<HashSet<string>> ObjectNamesAsync(RepoRef repo, string[] arguments, CancellationToken ct, string? stdin = null)
    {
        var output = (await RunAsync(GitDirOnly(repo, arguments) with { Stdin = stdin }, ct).ConfigureAwait(false)).StandardOutput;
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var end = line.IndexOf(' ', StringComparison.Ordinal);
            names.Add(end < 0 ? line.TrimEnd('\r') : line[..end]);
        }

        return names;
    }

    private async Task<bool> HoldsAnyAsync(RepoRef repo, string pack, HashSet<string> names, CancellationToken ct)
    {
        byte[] index;
        try
        {
            index = await File.ReadAllBytesAsync(Path.ChangeExtension(pack, ".idx"), ct).ConfigureAwait(false);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new GitOperationException("A pack's index could not be read before the reclaim: " + e.Message);
        }

        // "show-index" prints "offset name checksum" for each object.
        var listing = (await RunAsync(GitDirOnly(repo, "show-index") with { StdinBytes = index }, ct).ConfigureAwait(false)).StandardOutput;
        return listing.Split('\n', StringSplitOptions.RemoveEmptyEntries).Any(line => line.Split(' ') is [_, var name, ..] && names.Contains(name));
    }

    // A keep SavePoint wrote starts with its name; a zero-length one is a write a crash cut short. Nothing else here writes keeps
    // (no fetch, no automatic gc), so an empty one is ours too. Any other text belongs to someone else and stays.
    private static List<string> OwnKeepsOf(RepoRef repo)
    {
        if (!Directory.Exists(PackFolderOf(repo)))
        {
            return [];
        }

        try
        {
            return [.. Directory.EnumerateFiles(PackFolderOf(repo), "*.keep").Where(keep =>
            {
                var text = File.ReadAllText(keep);
                return text.Length == 0 || text.StartsWith(_keepTextStart, StringComparison.Ordinal);
            })];
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            throw new GitOperationException("A pack's keep file could not be read before the reclaim: " + e.Message);
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
