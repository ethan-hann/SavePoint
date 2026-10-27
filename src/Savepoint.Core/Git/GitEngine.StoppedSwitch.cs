using Savepoint.Core.Errors;

namespace Savepoint.Core.Git;

public sealed partial class GitEngine
{
    // What a switch that finishes a stopped one finds in the folder. The BlockingChanged and BlockingNew lists are unsaved work the finish
    // must not run over. Rewrite lists the files (in the target's spelling) the finish writes from the target, Keep those that already hold
    // the target's bytes and only need their index entry, and StaleSpellings the index entries under an old spelling of a name the target
    // spells another way (a drive that ignores case). Replaced is the part of Rewrite and Standing that holds content found in neither snapshot,
    // and RewriteBytes the size of the target's copy of each Rewrite file, in the same order. Standing lists the files that stand where the
    // point being left has a folder: git cannot move on from there while one stands (it takes the file for an untracked one in the way of
    // removing the folder's files), so the finish removes it and the checkout writes it from the target. Such a file may be the stopped
    // switch's write, whole or partial, or content the player put there since.
    private sealed record FinishScan(
        IReadOnlyList<string> BlockingChanged,
        IReadOnlyList<string> BlockingNew,
        IReadOnlyList<string> Rewrite,
        IReadOnlyList<long> RewriteBytes,
        IReadOnlyList<string> Keep,
        IReadOnlyList<string> StaleSpellings,
        IReadOnlyList<string> Replaced,
        IReadOnlyList<string> Standing)
    {
        public static FinishScan Nothing { get; } = new([], [], [], [], [], [], [], []);
    }

    /// <summary>
    /// Asks, without writing anything, what finishing the stopped switch the folder holds would run into: the unsaved changes that would stop
    /// the finish, and the files the finish would replace though they hold neither snapshot's copy. The switch is the one the note in the
    /// git directory names.
    /// </summary>
    /// <param name="repo">The folder.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <param name="listReplaced">
    /// True to compare the files the switch was writing with the snapshots (a file of the target's size is read in full) and list the ones that
    /// hold neither; false to skip that, which leaves <see cref="StoppedSwitchReview.ReplacedPaths"/> empty and reads no file.
    /// </param>
    /// <returns>The review; empty when the folder has no stopped switch or the note does not say where it was heading.</returns>
    /// <exception cref="Savepoint.Core.Errors.PathSafetyException">The work tree is a protected location.</exception>
    /// <exception cref="Savepoint.Core.Errors.PathMissingException">The work tree does not exist.</exception>
    /// <exception cref="Savepoint.Core.Errors.ConsentMarkerException">The folder has no marker for this game and role.</exception>
    /// <exception cref="Savepoint.Core.Errors.GitOperationException">The repository does not exist, the point the note names is not in it, or git failed.</exception>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    public async Task<StoppedSwitchReview> ReviewStoppedSwitchAsync(RepoRef repo, CancellationToken ct = default, bool listReplaced = true)
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

        if (await ReadNoteAsync(repo, ct).ConfigureAwait(false) is not { ToCommit: { } to } note)
        {
            return StoppedSwitchReview.None;
        }

        var head = GitDirFiles.ReadHead(repo.GitDir);
        var from = head.IsDetached ? head.DetachedCommit : await ResolveBranchAsync(repo, head.Branch!, ct).ConfigureAwait(false);
        var plan = await PlanSwitchAsync(repo, from, to, note, ct).ConfigureAwait(false);
        var scan = await ScanStoppedSwitchAsync(repo, from, plan, readFiles: listReplaced, ct).ConfigureAwait(false);
        return new StoppedSwitchReview(
            [.. scan.BlockingChanged.Concat(scan.BlockingNew).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)],
            [.. scan.Replaced.Order(StringComparer.Ordinal)]);
    }

    // Sorts what the folder holds against the stopped switch, reading and writing nothing in git. A change in a file the switch neither writes
    // nor removes is unsaved work, and so is an edit to a file the switch removes (it is the player's, and the finish would have to run over
    // it after writing the others). The files the switch was writing that differ are the ones git would refuse as local changes: with
    // readFiles they are sorted by whether they already hold the target's bytes, and size decides before any file is read. Without it only
    // the blocking changes are found, which takes no file read, and Rewrite, Keep and Replaced stay empty.
    private async Task<FinishScan> ScanStoppedSwitchAsync(RepoRef repo, string? from, SwitchPlan plan, bool readFiles, CancellationToken ct)
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

        if (!readFiles)
        {
            return new FinishScan(blockingChanged, blockingNew, [], [], [], [], [], []);
        }

        // Git for Windows lists what a junction leads to, not the junction, so a link in the old folder's place is found by looking.
        var foldersLeft = FoldersAbove(plan.Removed, comparer);
        foreach (var change in plan.WrittenChanges.Where(change => foldersLeft.Contains(change.Path) && IsLinkEntry(FullPath(repo, change.Path))))
        {
            candidates[change.Path] = change;
        }

        var sizes = await ObjectSizesAsync(repo, candidates.Values.Select(change => change.NewId), ct).ConfigureAwait(false);
        var trustMode = TrustsExecutableBit(repo);
        List<string> rewrite = [];
        List<long> rewriteBytes = [];
        List<string> keep = [];
        List<string> replaced = [];
        List<string> standing = [];
        foreach (var change in candidates.Values.OrderBy(change => change.Path, StringComparer.Ordinal))
        {
            ct.ThrowIfCancellationRequested();
            var holding = HoldsTarget(repo, change, sizes, trustMode, ct);
            if (foldersLeft.Contains(change.Path))
            {
                standing.Add(change.Path);
                if (holding == Holding.Other)
                {
                    replaced.Add(change.Path);
                }

                continue;
            }

            switch (holding)
            {
                case Holding.Target:
                    keep.Add(change.Path);
                    break;
                case Holding.Other:
                    rewrite.Add(change.Path);
                    rewriteBytes.Add(sizes.GetValueOrDefault(change.NewId));
                    replaced.Add(change.Path);
                    break;
                default:
                    rewrite.Add(change.Path);
                    rewriteBytes.Add(sizes.GetValueOrDefault(change.NewId));
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
        return new FinishScan(blockingChanged, blockingNew, rewrite, rewriteBytes, keep, stale, replaced, standing);
    }

    // Every folder that holds a file in the list, however deep.
    private static HashSet<string> FoldersAbove(IEnumerable<string> paths, StringComparer comparer)
    {
        HashSet<string> folders = new(comparer);
        foreach (var path in paths)
        {
            for (var slash = path.LastIndexOf('/'); slash > 0; slash = path.LastIndexOf('/', slash - 1))
            {
                if (!folders.Add(path[..slash]))
                {
                    break;
                }
            }
        }

        return folders;
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

    // Whether a file's executable bit is part of what git compares. git decides when the repository is made: a drive that keeps no
    // permissions (a Windows drive under WSL) shows every file as executable, so git is told to ignore the bit (core.filemode false), and
    // then a file with the right bytes is the right file whatever its bit says. Windows has no bit.
    private static bool TrustsExecutableBit(RepoRef repo)
    {
        if (OperatingSystem.IsWindows())
        {
            return false;
        }

        var text = AtomicText.ReadOrNull(RepoConfigFile.ConfigPath(repo.GitDir));
        return !string.Equals(text is null ? null : RepoConfigFile.ReadValue(text, "core", "filemode"), "false", StringComparison.OrdinalIgnoreCase);
    }

    // Whether the file the target wants at this path already holds the target's bytes. A file of another size cannot, so only files of the
    // right size are read.
    private Holding HoldsTarget(RepoRef repo, TreeChange change, Dictionary<string, long> sizes, bool trustMode, CancellationToken ct)
    {
        if (change.NewMode is not ("100644" or "100755" or "120000") || !sizes.TryGetValue(change.NewId, out var size))
        {
            return Holding.Nothing;
        }

        // On Windows git writes a link as a plain file holding its text, which is compared like any other file below.
        if (change.NewMode == "120000" && HoldsLink(repo, change.Path, change.NewId) is { } link)
        {
            return link;
        }

        var full = FullPath(repo, change.Path);
        try
        {
            var file = new FileInfo(full);

            // FileInfo.Exists is false for a link to a folder, so a link is looked for first: it is never a file with the target's bytes.
            if (file.LinkTarget is not null)
            {
                return Holding.Other;
            }

            if (!file.Exists)
            {
                return Holding.Nothing;
            }

            if ((file.Attributes & FileAttributes.ReparsePoint) != 0 || file.Length != size)
            {
                return Holding.Other;
            }

            // The mode is part of what the target says, so a file with the right bytes and the wrong mode is written again, though it is not another content.
            if (trustMode && !OperatingSystem.IsWindows() && ((File.GetUnixFileMode(full) & UnixFileMode.UserExecute) != 0) != (change.NewMode == "100755"))
            {
                return Holding.Nothing;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Holding.Nothing;
        }

        _options.FileHashed?.Invoke(full);
        return BlobHasher.TryHash(full, out var id, ct) switch
        {
            HashOutcome.Hashed => string.Equals(id, change.NewId, StringComparison.Ordinal) ? Holding.Target : Holding.Other,
            _ => Holding.Nothing,
        };
    }

    // What stands where the target wants a link, or null when it is a plain file on Windows, which the caller compares. A link is stored
    // as the text of its target and is never read through, so a link is compared by the id git would give that text. The same link is the
    // stopped switch's own write and a folder is the old point's, neither of which is anyone's content; the finish writes the link again
    // either way. Anything else standing there is content the finish replaces: a file, or a link to somewhere else. Windows writes a link
    // as a text file (core.symlinks is off), so a real link or junction found there was never the switch's.
    private static Holding? HoldsLink(RepoRef repo, string path, string expectedId)
    {
        try
        {
            var info = new FileInfo(FullPath(repo, path));
            if (info.LinkTarget is { } linked)
            {
                return !OperatingSystem.IsWindows() && string.Equals(BlobHasher.IdOf(System.Text.Encoding.UTF8.GetBytes(linked)), expectedId, StringComparison.Ordinal)
                    ? Holding.Nothing
                    : Holding.Other;
            }

            if (!info.Exists)
            {
                return Holding.Nothing;
            }

            return OperatingSystem.IsWindows() ? null : Holding.Other;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return Holding.Nothing;
        }
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
            RemoveStanding(repo, scan.Standing);
            if (scan.Rewrite.Count > 0)
            {
                await WriteTargetCopiesAsync(repo, to, scan, reporter, ct).ConfigureAwait(false);
            }

            if (scan.Keep.Count > 0)
            {
                await RunWriteAsync(() => Invocation(repo, readOnly: false, "reset", "--quiet", to).WithPathspecs(scan.Keep), ct).ConfigureAwait(false);
            }

            await WriteIndexInfoAsync(repo, scan.StaleSpellings, [], new Dictionary<string, string?>(), ct).ConfigureAwait(false);
        }
        catch (SavepointException)
        {
            // Some batches of files may be written and the next one not, and a step may fail after the files before it were written. That is
            // safe: the note stays, so repeating the switch finds those files as a kill would have left them and finishes the job. The index
            // goes back as the alignment found it.
            await TryRestoreAsync(repo, aligned).ConfigureAwait(false);
            throw;
        }
    }

    // Takes away the files and links that stand where the point being left has a folder (see FinishScan); the checkout that follows
    // writes each one from the target. An entry that is not a file or a link in the approved folder (a folder above it became a link after
    // the scan, or another file now has its name in other letter case) is not ours to remove. A link is removed as the link, never
    // through it, so what it leads to is untouched.
    private void RemoveStanding(RepoRef repo, IReadOnlyList<string> standing)
    {
        if (standing.Count == 0)
        {
            return;
        }

        RecheckBeforeFileChanges(repo, "remove-standing");
        var approved = repo.Guard is { } guard ? repo with { WorkTree = guard.RealPath } : repo;
        foreach (var path in standing)
        {
            var full = FullPath(approved, path);
            var isLink = IsLinkEntry(full);
            if (!isLink && !File.Exists(full))
            {
                continue;
            }

            if (LinkedFolders.IsInsideLink(approved.WorkTree, path) || !HasExactName(full))
            {
                throw new DirtyWorkingTreeException(repo.Role);
            }

            if (isLink)
            {
                TryDeleteLink(full);
            }
            else
            {
                TryDeleteReadOnly(full);
            }

            if (File.Exists(full) || IsLinkEntry(full))
            {
                throw !isLink && FileLockProbe.Probe(full) is { } kind
                    ? new HeldFile(FullPath(repo, path), kind).ToException(HeldFileOperation.Switch)
                    : new GitOperationException(FullPath(repo, path), NotUpdatedMessage(FileStep.RemoveStanding, [path]));
            }
        }
    }

    // True for a link to a file or a folder, a dangling one included.
    private static bool IsLinkEntry(string full)
    {
        try
        {
            return new FileInfo(full).LinkTarget is not null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    // Deletes the link itself. Unix unlinks any link; Windows takes a link to a folder away as a folder.
    private static void TryDeleteLink(string full)
    {
        try
        {
            if (OperatingSystem.IsWindows() && Directory.Exists(full))
            {
                Directory.Delete(full, recursive: false);
            }
            else
            {
                File.Delete(full);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Still in use; the check after it reports the link as still there.
        }
    }

    // git prints no meter when it checks out paths, so a switch that reports progress gets its counts from the batches the paths go in.
    private const int _finishBatch = 200;

    private async Task WriteTargetCopiesAsync(RepoRef repo, string to, FinishScan scan, ProgressReporter? reporter, CancellationToken ct)
    {
        var paths = scan.Rewrite;
        if (reporter is null)
        {
            await WriteTargetBatchAsync(repo, to, paths, ct).ConfigureAwait(false);
            return;
        }

        long bytes = 0;
        for (var done = 0; done < paths.Count; done += _finishBatch)
        {
            ct.ThrowIfCancellationRequested();
            var batch = Math.Min(_finishBatch, paths.Count - done);
            await WriteTargetBatchAsync(repo, to, [.. paths.Skip(done).Take(batch)], ct).ConfigureAwait(false);
            bytes += scan.RewriteBytes.Skip(done).Take(batch).Sum();
            reporter.ObserveFinishing(done + batch, paths.Count, bytes);
        }
    }

    // The caller puts the index back when this throws.
    private async Task WriteTargetBatchAsync(RepoRef repo, string to, IReadOnlyList<string> paths, CancellationToken ct)
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

                throw new GitOperationException(GitFailure.LockFilePath(stderr), $"git could not take its lock: {stderr.Trim()}");
            }

            if (GitFailure.IsDiskFull(stderr))
            {
                throw new DiskSpaceException(repo.WorkTree, _options.MinFreeBytesForCheckout, _freeSpace(repo.WorkTree));
            }

            // A file another program holds could not be replaced, but git has already moved its index entry to the target's, so the checkout
            // that follows sees nothing to do for it. The verify pass after that finds the file differing, tries again, and names it if it stays.
            if (!GitFailure.FileCouldNotBeReplaced(stderr))
            {
                throw GitFailure.ToException(new GitCommandException(["checkout", to], result.ExitCode, stderr));
            }

            return;
        }
    }
}
