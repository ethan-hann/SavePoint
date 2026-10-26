using Savepoint.Core.Errors;

namespace Savepoint.Core.Git;

public sealed partial class GitEngine
{
    // What a switch that finishes a stopped one finds in the folder. The BlockingChanged and BlockingNew lists are unsaved work the finish
    // must not run over. Rewrite lists the files (in the target's spelling) the finish writes from the target, Keep those that already hold
    // the target's bytes and only need their index entry, and StaleSpellings the index entries under an old spelling of a name the target
    // spells another way (a drive that ignores case). Replaced is the part of Rewrite that holds content found in neither snapshot.
    private sealed record FinishScan(
        IReadOnlyList<string> BlockingChanged,
        IReadOnlyList<string> BlockingNew,
        IReadOnlyList<string> Rewrite,
        IReadOnlyList<string> Keep,
        IReadOnlyList<string> StaleSpellings,
        IReadOnlyList<string> Replaced)
    {
        public static FinishScan Nothing { get; } = new([], [], [], [], [], []);
    }

    /// <summary>
    /// Asks, without writing anything, what finishing the stopped switch the folder holds would run into: the unsaved changes that would stop
    /// the finish, and the files the finish would replace though they hold neither snapshot's copy. The switch is the one the note in the
    /// git directory names.
    /// </summary>
    /// <param name="repo">The folder.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>The review; empty when the folder has no stopped switch or the note does not say where it was heading.</returns>
    /// <exception cref="Savepoint.Core.Errors.PathSafetyException">The work tree is a protected location.</exception>
    /// <exception cref="Savepoint.Core.Errors.PathMissingException">The work tree does not exist.</exception>
    /// <exception cref="Savepoint.Core.Errors.ConsentMarkerException">The folder has no marker for this game and role.</exception>
    /// <exception cref="Savepoint.Core.Errors.GitOperationException">The repository does not exist, the point the note names is not in it, or git failed.</exception>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    public async Task<StoppedSwitchReview> ReviewStoppedSwitchAsync(RepoRef repo, CancellationToken ct = default)
    {
        repo = Normalize(repo);
        ct.ThrowIfCancellationRequested();

        // Like the switch check, nothing is written, so the folder is not pinned or held.
        CheckWorkTree(repo);
        RepoMarker.Verify(repo);
        if (!IsInitialized(repo.GitDir))
        {
            throw new GitOperationException($"The repository at '{repo.GitDir}' does not exist.");
        }

        if (SwitchJournal.Read(repo.GitDir) is not { ToCommit: { } to } note)
        {
            return StoppedSwitchReview.None;
        }

        var head = GitDirFiles.ReadHead(repo.GitDir);
        var from = head.IsDetached ? head.DetachedCommit : await ResolveBranchAsync(repo, head.Branch!, ct).ConfigureAwait(false);
        var plan = await PlanSwitchAsync(repo, from, to, note, ct).ConfigureAwait(false);
        var scan = await ScanStoppedSwitchAsync(repo, from, plan, ct).ConfigureAwait(false);
        return new StoppedSwitchReview(
            [.. scan.BlockingChanged.Concat(scan.BlockingNew).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)],
            [.. scan.Replaced.Order(StringComparer.Ordinal)]);
    }

    // Sorts what the folder holds against the stopped switch, reading and writing nothing in git. A change in a file the switch neither writes
    // nor removes is unsaved work, and so is an edit to a file the switch removes (it is the player's, and the finish would have to run over
    // it after writing the others). The files the switch was writing that differ are the ones git would refuse as local changes: they are
    // sorted by whether they already hold the target's bytes, and size decides before any file is read.
    private async Task<FinishScan> ScanStoppedSwitchAsync(RepoRef repo, string? from, SwitchPlan plan, CancellationToken ct)
    {
        var changedTask = ListChangedAsync(repo, hasCommits: from is not null, ct);
        var untrackedTask = ListNewAsync(repo, ct);
        var changedPaths = await changedTask.ConfigureAwait(false);
        var newFiles = (await untrackedTask.ConfigureAwait(false)).Files;

        // A stopped switch may have aligned the index for the files the patterns name, which makes those differ from HEAD; they are never in the way.
        var comparer = PathComparerFor(repo);
        var inSwitch = plan.Touched.Concat(plan.Named).ToHashSet(comparer);
        var removedByTheSwitch = plan.Gone.Select(gone => gone.Path).ToHashSet(comparer);
        var blockingChanged = changedPaths.Where(path => !inSwitch.Contains(path) || (removedByTheSwitch.Contains(path) && File.Exists(FullPath(repo, path)))).ToArray();
        var blockingNew = newFiles.Where(path => !inSwitch.Contains(path)).ToArray();

        // Git lists a file in the spelling the index has; the target may spell it another way on a drive that ignores case.
        var writtenBy = new Dictionary<string, TreeChange>(comparer);
        foreach (var change in plan.WrittenChanges)
        {
            writtenBy.TryAdd(change.Path, change);
        }

        var candidates = new Dictionary<string, TreeChange>(StringComparer.Ordinal);
        foreach (var path in changedPaths.Concat(newFiles))
        {
            if (writtenBy.TryGetValue(path, out var change))
            {
                candidates[change.Path] = change;
            }
        }

        var sizes = await ObjectSizesAsync(repo, candidates.Values.Select(change => change.NewId), ct).ConfigureAwait(false);
        List<string> rewrite = [];
        List<string> keep = [];
        List<string> replaced = [];
        foreach (var change in candidates.Values.OrderBy(change => change.Path, StringComparer.Ordinal))
        {
            ct.ThrowIfCancellationRequested();
            switch (HoldsTarget(repo, change, sizes))
            {
                case Holding.Target:
                    keep.Add(change.Path);
                    break;
                case Holding.Other:
                    rewrite.Add(change.Path);
                    replaced.Add(change.Path);
                    break;
                default:
                    rewrite.Add(change.Path);
                    break;
            }
        }

        var removedBy = new Dictionary<string, string>(comparer);
        foreach (var path in plan.Removed)
        {
            removedBy.TryAdd(path, path);
        }

        var stale = rewrite.Concat(keep)
            .Select(path => removedBy.TryGetValue(path, out var old) && !string.Equals(old, path, StringComparison.Ordinal) ? old : null)
            .OfType<string>()
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        return new FinishScan(blockingChanged, blockingNew, rewrite, keep, stale, replaced);
    }

    private enum Holding
    {
        // Nothing to compare: the file is missing, is not an ordinary file, or cannot be read; the finish writes it.
        Nothing,

        // The file holds the target's bytes.
        Target,

        // The file holds something else, which the finish replaces.
        Other,
    }

    // Whether the file the target wants at this path already holds the target's bytes. A file of another size cannot, so only files of the
    // right size are read.
    private static Holding HoldsTarget(RepoRef repo, TreeChange change, Dictionary<string, long> sizes)
    {
        if (change.NewMode is not ("100644" or "100755") || !sizes.TryGetValue(change.NewId, out var size))
        {
            return Holding.Nothing;
        }

        var full = FullPath(repo, change.Path);
        try
        {
            var file = new FileInfo(full);
            if (!file.Exists)
            {
                return Holding.Nothing;
            }

            if ((file.Attributes & FileAttributes.ReparsePoint) != 0 || file.Length != size)
            {
                return Holding.Other;
            }

            // The mode is part of what the target says, so a file with the right bytes and the wrong mode is written again, though it is not another content.
            if (!OperatingSystem.IsWindows() && ((File.GetUnixFileMode(full) & UnixFileMode.UserExecute) != 0) != (change.NewMode == "100755"))
            {
                return Holding.Nothing;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Holding.Nothing;
        }

        return BlobHasher.TryHash(full, out var id) switch
        {
            HashOutcome.Hashed => string.Equals(id, change.NewId, StringComparison.Ordinal) ? Holding.Target : Holding.Other,
            _ => Holding.Nothing,
        };
    }

    // A switch that was killed while git wrote files leaves HEAD and the index at the old point and some files already at the new one, so a
    // plain checkout reads them as local changes and refuses, and the switch could never be finished. Checking out just those paths from the
    // target does not look at what is in the files: it writes the target's copy and its index entry, which is what the stopped switch was
    // doing. A file that already holds the target's bytes is not written again; only its index entry is made to say what the target says.
    // On a drive that ignores case the old spelling's index entry goes, or the checkout that follows would see it as an edited file.
    // Nothing else is forced, so the checkout that follows still refuses a change anywhere else. The note is the proof that this switch is
    // the one that was running; it is never cleared here, and a failure leaves it.
    private async Task WriteFinishingPathsAsync(RepoRef repo, string to, FinishScan scan, AlignedIndex? aligned, ProgressReporter? reporter, CancellationToken ct)
    {
        try
        {
            if (scan.Rewrite.Count > 0)
            {
                await WriteTargetCopiesAsync(repo, to, scan.Rewrite, aligned, reporter, ct).ConfigureAwait(false);
            }

            if (scan.Keep.Count > 0)
            {
                await RunWriteAsync(() => Invocation(repo, readOnly: false, "reset", "--quiet", to).WithPathspecs(scan.Keep), ct).ConfigureAwait(false);
            }

            await WriteIndexInfoAsync(repo, scan.StaleSpellings, [], new Dictionary<string, string?>(), ct).ConfigureAwait(false);
        }
        catch (Exception e) when (e is FolderChangedException or ConsentMarkerException or GitOperationException)
        {
            // Nothing is half done by these: they come before git starts or from a step that only touches the index. The index goes back
            // as the alignment found it.
            await TryRestoreAsync(repo, aligned).ConfigureAwait(false);
            throw;
        }
    }

    // git prints no meter when it checks out paths, so a switch that reports progress gets its counts from the batches the paths go in.
    private const int _finishBatch = 200;

    private async Task WriteTargetCopiesAsync(RepoRef repo, string to, IReadOnlyList<string> paths, AlignedIndex? aligned, ProgressReporter? reporter, CancellationToken ct)
    {
        if (reporter is null)
        {
            await WriteTargetBatchAsync(repo, to, paths, aligned, ct).ConfigureAwait(false);
            return;
        }

        for (var done = 0; done < paths.Count; done += _finishBatch)
        {
            ct.ThrowIfCancellationRequested();
            await WriteTargetBatchAsync(repo, to, [.. paths.Skip(done).Take(_finishBatch)], aligned, ct).ConfigureAwait(false);
            reporter.Observe(Math.Min(done + _finishBatch, paths.Count), paths.Count);
        }
    }

    private async Task WriteTargetBatchAsync(RepoRef repo, string to, IReadOnlyList<string> paths, AlignedIndex? aligned, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            var result = await RunRawAsync(Invocation(repo, readOnly: false, "checkout", to).WithPathspecs(paths) with { AllowedExitCodes = _anyExitCode }, ct).ConfigureAwait(false);
            if (result.ExitCode == 0)
            {
                return;
            }

            var stderr = result.StandardError;
            if (GitFailure.IsLockContention(stderr))
            {
                if (attempt < _options.LockRetryDelays.Count)
                {
                    await Task.Delay(_options.LockRetryDelays[attempt], ct).ConfigureAwait(false);
                    continue;
                }

                await TryRestoreAsync(repo, aligned).ConfigureAwait(false);
                throw new GitOperationException(GitFailure.LockFilePath(stderr), $"git could not take its lock: {stderr.Trim()}");
            }

            if (GitFailure.IsDiskFull(stderr))
            {
                throw new DiskSpaceException(repo.WorkTree, _options.MinFreeBytesForCheckout, _freeSpace(repo.WorkTree));
            }

            // A file another program holds stays as it is; the checkout that follows names it.
            if (!GitFailure.FileCouldNotBeReplaced(stderr))
            {
                throw GitFailure.ToException(new GitCommandException(["checkout", to], result.ExitCode, stderr));
            }

            return;
        }
    }
}
