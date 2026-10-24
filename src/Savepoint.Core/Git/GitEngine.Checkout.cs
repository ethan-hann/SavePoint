using System.Text.RegularExpressions;
using Savepoint.Core.Errors;

namespace Savepoint.Core.Git;

public sealed partial class GitEngine
{
    private const int _namedPathsInMessage = 20;

    private static readonly int[] _anyExitCode = [.. Enumerable.Range(0, 256)];

    /// <summary>
    /// Switches the folder to a branch: git rewrites the files that differ, then the engine verifies that the files the
    /// switch changed match the point and re-syncs any that stayed behind (a file another program held open). The switch
    /// refuses to start when anything is in the way: unsaved changes in the folder (edited, deleted or new files the game's
    /// patterns do not name), a folder link on Windows, or a folder a stopped switch left half done (only that same switch may
    /// finish it). Nothing is written then. A file the game's patterns name is never in the way and never touched: the switch
    /// does not write it, remove it or restore it, whether or not either point holds it. A path that is a file in one place and a
    /// folder in the other cannot be switched while the folder holds a file the switch does not remove itself (one the patterns
    /// name, say), or while a file of the player's stands where a folder is needed: the switch stops before it writes anything.
    /// </summary>
    /// <param name="repo">The folder to switch.</param>
    /// <param name="branch">The profile's branch; it must have at least one point.</param>
    /// <param name="progress">Receives git's own file counts; may be null.</param>
    /// <param name="ct">Cancels the switch. git and its children are stopped and the switch is reported as unfinished until repeated or discarded.</param>
    /// <exception cref="Savepoint.Core.Errors.PathSafetyException">The work tree is a protected location.</exception>
    /// <exception cref="Savepoint.Core.Errors.ConsentMarkerException">The folder has no marker for this game and role.</exception>
    /// <exception cref="Savepoint.Core.Errors.PathMissingException">The work tree does not exist.</exception>
    /// <exception cref="Savepoint.Core.Errors.DiskSpaceException">The work tree's volume cannot take the files the switch adds (nothing was written), or the disk filled during the switch (it is then left unfinished; see <see cref="RepoStatus.InterruptedSwitch"/>).</exception>
    /// <exception cref="Savepoint.Core.Errors.DirtyWorkingTreeException">Unsaved changes, or a file the game's patterns name, are in the way; nothing was changed.</exception>
    /// <exception cref="Savepoint.Core.Errors.InterruptedSwitchException">A stopped switch left the folder half done and this one is not the switch to finish it; nothing was changed.</exception>
    /// <exception cref="Savepoint.Core.Errors.LinkedFolderException">On Windows, a folder link lies in the way; nothing was changed.</exception>
    /// <exception cref="Savepoint.Core.Errors.FileFolderClashException">Writing a file would delete a folder holding files of the player's that the switch does not remove, or making a folder would delete a file of the player's; nothing was changed.</exception>
    /// <exception cref="Savepoint.Core.Errors.ProfileHasNoPointsException">The branch has no point to switch to.</exception>
    /// <exception cref="Savepoint.Core.Errors.GitOperationException">A file stayed in use through every attempt (the path is in its arguments), the only files reported as changed are held open by another program so they could not be read, or git failed.</exception>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    public Task CheckoutBranchAsync(RepoRef repo, string branch, IProgress<FileProgress>? progress = null, CancellationToken ct = default)
    {
        BranchNames.Require(branch);
        return CheckoutAsync(repo, branch, isBranch: true, force: false, progress, ct);
    }

    /// <summary>
    /// Switches the folder to an older point, leaving HEAD detached. Behaves like <see cref="CheckoutBranchAsync"/> otherwise.
    /// </summary>
    /// <param name="repo">The folder to switch.</param>
    /// <param name="commitId">The full id of a point in the repository.</param>
    /// <param name="progress">Receives git's own file counts; may be null.</param>
    /// <param name="ct">Cancels the switch.</param>
    /// <exception cref="ArgumentException"><paramref name="commitId"/> is not a full commit id.</exception>
    public Task CheckoutCommitAsync(RepoRef repo, string commitId, IProgress<FileProgress>? progress = null, CancellationToken ct = default)
    {
        RequireCommitId(commitId);
        return CheckoutAsync(repo, commitId, isBranch: false, force: false, progress, ct);
    }

    private static void RequireCommitId(string? commitId)
    {
        if (commitId is null || !GitDirFiles.IsObjectId(commitId))
        {
            throw new ArgumentException("A full commit id is needed.", nameof(commitId));
        }
    }

    /// <summary>
    /// Switches the folder to a branch and makes it match, discarding unsaved changes to recorded files and overwriting new
    /// files that are in the way. It still refuses a half-switched folder it is not finishing and a folder link on Windows, and it
    /// still leaves the files the game's patterns name as they are. It also refuses a file and a folder that cannot share a path
    /// while the player's own files that the patterns name (or a repository of its own) would be deleted with the folder. Everything
    /// else behaves like <see cref="CheckoutBranchAsync"/>.
    /// </summary>
    /// <param name="repo">The folder to switch.</param>
    /// <param name="branch">The profile's branch; it must have at least one point.</param>
    /// <param name="ct">Cancels the switch.</param>
    public Task ForceCheckoutBranchAsync(RepoRef repo, string branch, CancellationToken ct = default)
    {
        BranchNames.Require(branch);
        return CheckoutAsync(repo, branch, isBranch: true, force: true, progress: null, ct);
    }

    /// <summary>
    /// Switches the folder to an older point and makes it match, discarding local changes like <see cref="ForceCheckoutBranchAsync"/>.
    /// </summary>
    /// <param name="repo">The folder to switch.</param>
    /// <param name="commitId">The full id of a point in the repository.</param>
    /// <param name="ct">Cancels the switch.</param>
    public Task ForceCheckoutCommitAsync(RepoRef repo, string commitId, CancellationToken ct = default)
    {
        RequireCommitId(commitId);
        return CheckoutAsync(repo, commitId, isBranch: false, force: true, progress: null, ct);
    }

    /// <summary>
    /// Throws away every change since the checked-out point: modified and deleted files come back, new files that
    /// the game's ignore patterns do not name are removed (an ignore file inside the game folder protects nothing).
    /// Files those patterns name stay as they are on disk, recorded by the point or not, and so does the consent marker. When the point holds
    /// a file where the folder has a folder (or a folder where the player has a file), and what would be deleted with it includes a file
    /// those patterns name or a repository of its own, the discard stops before it writes anything. Works on a detached HEAD, and also finishes
    /// the job of an unfinished switch by returning the folder to the point HEAD is on (which is the point the switch
    /// started from unless HEAD had already moved; <see cref="RepoStatus.InterruptedSwitch"/> says which).
    /// </summary>
    /// <param name="repo">The folder.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <exception cref="Savepoint.Core.Errors.PathSafetyException">The work tree is a protected location.</exception>
    /// <exception cref="Savepoint.Core.Errors.PathMissingException">The work tree does not exist.</exception>
    /// <exception cref="Savepoint.Core.Errors.ConsentMarkerException">The folder has no marker for this game and role.</exception>
    /// <exception cref="Savepoint.Core.Errors.ProfileHasNoPointsException">The folder has no saved point to return to; without one a discard would delete the whole folder, so nothing is touched.</exception>
    /// <exception cref="Savepoint.Core.Errors.LinkedFolderException">On Windows, a folder link lies in the way; nothing was changed.</exception>
    /// <exception cref="Savepoint.Core.Errors.FileFolderClashException">Restoring a file would delete a folder holding files of the player's that the discard does not remove, or restoring a folder would delete a file of the player's that the patterns name; nothing was changed.</exception>
    /// <exception cref="Savepoint.Core.Errors.DiskSpaceException">The disk filled while files were restored.</exception>
    /// <exception cref="Savepoint.Core.Errors.GitOperationException">A file stayed in use through every attempt, or git failed.</exception>
    /// <remarks>A point taken since empty folders were recorded holds its own, and a discard makes them again (git does not track them). A discard removes the empty folders the point does not hold along with the new files, and every empty folder of a point taken before that.</remarks>
    public async Task DiscardChangesAsync(RepoRef repo, CancellationToken ct = default)
    {
        repo = Normalize(repo);
        ct.ThrowIfCancellationRequested();
        CheckWorkTree(repo);
        RepoMarker.Verify(repo);
        if (!IsInitialized(repo.GitDir))
        {
            throw new GitOperationException($"The repository at '{repo.GitDir}' does not exist.");
        }

        var point = await RequireSavedPointAsync(repo, ct).ConfigureAwait(false);
        await RefuseLinkedFoldersInChangesAsync(repo, includeChanged: true, ct).ConfigureAwait(false);
        await RefuseDiscardClashAsync(repo, ct).ConfigureAwait(false);

        using var scope = BeginWrite(repo);

        var named = await ResetToPointAsync(repo, ct).ConfigureAwait(false);
        await VerifyAsync(repo, point, [], null, named, ct).ConfigureAwait(false);
        await RemoveNewFilesAsync(repo, ct).ConfigureAwait(false);
        await RestoreEmptyFoldersAsync(repo, point, ct).ConfigureAwait(false);
        SwitchJournal.Clear(repo.GitDir);
    }

    // Writes the point's copy of every file that differs and returns the files the patterns name, which are left as they are. With no
    // patterns that is one hard reset. With patterns the index goes back to the point first, which changes no file, and only the
    // differing files they do not name are written: a hard reset would write the point's copy of a named file as well.
    private async Task<IReadOnlySet<string>> ResetToPointAsync(RepoRef repo, CancellationToken ct)
    {
        if (!HasPatterns(repo))
        {
            await RunRewriteAsync(Invocation(repo, readOnly: false, "reset", "--hard", "--quiet"), ["reset", "--hard"], ct).ConfigureAwait(false);
            return new HashSet<string>();
        }

        await RunWriteAsync(() => Invocation(repo, readOnly: false, "reset", "--quiet"), ct).ConfigureAwait(false);
        var named = await RecordedNamedAsync(repo, ct).ConfigureAwait(false);
        var differing = (await DiffNamesAsync(repo, "HEAD", ct).ConfigureAwait(false)).Where(path => !named.Contains(path)).ToArray();
        if (differing.Length > 0)
        {
            await RunRewriteAsync(Invocation(repo, readOnly: false, "checkout", "HEAD").WithPathspecs(differing), ["checkout", "HEAD"], ct).ConfigureAwait(false);
        }

        return named;
    }

    // A file another program holds fails the command for that file only; the verify pass after it finds the file and tries again.
    private async Task RunRewriteAsync(GitInvocation invocation, string[] command, CancellationToken ct)
    {
        var result = await RunRawAsync(invocation with { AllowedExitCodes = _anyExitCode }, ct).ConfigureAwait(false);
        if (result.ExitCode == 0)
        {
            return;
        }

        if (GitFailure.IsDiskFull(result.StandardError))
        {
            throw new DiskSpaceException(invocation.WorkTree ?? string.Empty, _options.MinFreeBytesForCheckout, _freeSpace(invocation.WorkTree ?? string.Empty));
        }

        if (!GitFailure.FileCouldNotBeReplaced(result.StandardError))
        {
            throw GitFailure.ToException(new GitCommandException(command, result.ExitCode, result.StandardError));
        }
    }

    /// <summary>
    /// Removes new files and folders; changes to recorded files stay. Files the game's ignore patterns name (those
    /// <see cref="RepoRef.IgnorePatterns"/> carries, or the ones the last capture or preview stored), the consent marker and folders that keep their own history stay. An ignore file inside the
    /// game folder protects nothing. The empty folders the point holds are made again if the clean removed them.
    /// </summary>
    /// <param name="repo">The folder.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <exception cref="Savepoint.Core.Errors.PathSafetyException">The work tree is a protected location.</exception>
    /// <exception cref="Savepoint.Core.Errors.PathMissingException">The work tree does not exist.</exception>
    /// <exception cref="Savepoint.Core.Errors.ConsentMarkerException">The folder has no marker for this game and role.</exception>
    /// <exception cref="Savepoint.Core.Errors.ProfileHasNoPointsException">The folder has no saved point, so every file in it would count as new and be removed; nothing is touched.</exception>
    /// <exception cref="Savepoint.Core.Errors.LinkedFolderException">On Windows, a folder link lies in the way; nothing was changed.</exception>
    /// <exception cref="Savepoint.Core.Errors.GitOperationException">A file stayed in use through every attempt, or git failed.</exception>
    public async Task CleanAsync(RepoRef repo, CancellationToken ct = default)
    {
        repo = Normalize(repo);
        ct.ThrowIfCancellationRequested();
        CheckWorkTree(repo);
        RepoMarker.Verify(repo);
        if (!IsInitialized(repo.GitDir))
        {
            throw new GitOperationException($"The repository at '{repo.GitDir}' does not exist.");
        }

        // Before the first point every file in the folder is "new"; cleaning would delete the player's whole folder.
        var point = await RequireSavedPointAsync(repo, ct).ConfigureAwait(false);
        await RefuseLinkedFoldersInChangesAsync(repo, includeChanged: false, ct).ConfigureAwait(false);

        using var scope = BeginWrite(repo);
        await RemoveNewFilesAsync(repo, ct).ConfigureAwait(false);
        await RestoreEmptyFoldersAsync(repo, point, ct).ConfigureAwait(false);
    }

    // A discard rewrites the changed files and a clean removes the new ones: neither may go through a folder link (Windows).
    private async Task RefuseLinkedFoldersInChangesAsync(RepoRef repo, bool includeChanged, CancellationToken ct)
    {
        if (!LinkedFolders.Enabled)
        {
            return;
        }

        var untracked = ListNewAsync(repo, ct);
        var changed = includeChanged ? await ListChangedAsync(repo, hasCommits: true, ct).ConfigureAwait(false) : [];
        LinkedFolders.RefuseAny(repo.WorkTree, changed.Concat((await untracked.ConfigureAwait(false)).Files), ct);
    }

    // The point the folder is on; a folder with none has nothing a discard or a clean could be measured against.
    private async Task<string> RequireSavedPointAsync(RepoRef repo, CancellationToken ct)
    {
        var head = GitDirFiles.ReadHead(repo.GitDir);
        var point = head.IsDetached ? head.DetachedCommit : await ResolveBranchAsync(repo, head.Branch!, ct).ConfigureAwait(false);
        return point ?? throw new ProfileHasNoPointsException(head.Branch ?? string.Empty);
    }

    // Removes new files that SavePoint's exclude list does not name; a file still in use is retried and then named. The list
    // is passed as -e patterns with -x, so an ignore file inside the game folder protects nothing.
    private async Task RemoveNewFilesAsync(RepoRef repo, CancellationToken ct)
    {
        List<string> clean = ["clean", "-f", "-d", "-x", "--quiet"];
        foreach (var pattern in ExcludePatterns(repo))
        {
            clean.Add("-e");
            clean.Add(pattern);
        }

        await RunRawAsync(Invocation(repo, readOnly: false, [.. clean]) with { AllowedExitCodes = _anyExitCode }, ct).ConfigureAwait(false);
        for (var pass = 0; ; pass++)
        {
            ct.ThrowIfCancellationRequested();

            // A folder that is a repository of its own stays; clean never removes one.
            var left = (await ListNewAsync(repo, ct).ConfigureAwait(false)).Files;
            if (left.Count == 0)
            {
                return;
            }

            if (pass >= _options.ResyncDelays.Count)
            {
                throw new GitOperationException(FullPath(repo, left[0]), NotUpdatedMessage(left));
            }

            foreach (var path in left)
            {
                TryDelete(FullPath(repo, path));
            }

            await Task.Delay(_options.ResyncDelays[pass], ct).ConfigureAwait(false);
        }
    }

    private async Task CheckoutAsync(RepoRef repo, string target, bool isBranch, bool force, IProgress<FileProgress>? progress, CancellationToken ct)
    {
        repo = Normalize(repo);
        ct.ThrowIfCancellationRequested();
        CheckWorkTree(repo);
        RepoMarker.Verify(repo);
        if (!IsInitialized(repo.GitDir))
        {
            throw new GitOperationException($"The repository at '{repo.GitDir}' does not exist.");
        }

        var head = GitDirFiles.ReadHead(repo.GitDir);
        var from = head.IsDetached ? head.DetachedCommit : await ResolveBranchAsync(repo, head.Branch!, ct).ConfigureAwait(false);
        var to = isBranch ? await ResolveBranchAsync(repo, target, ct).ConfigureAwait(false) : await ResolveCommitAsync(repo, target, ct).ConfigureAwait(false);
        if (to is null)
        {
            throw isBranch
                ? new ProfileHasNoPointsException(target)
                : new GitOperationException($"The point '{target}' is not in the repository.");
        }

        // Which files the switch will write and remove is known before it starts: nothing unsaved may be in the way, and a
        // file that cannot be removed is found afterwards.
        var note = SwitchJournal.Read(repo.GitDir);
        var plan = await PlanSwitchAsync(repo, from, to, note, ct).ConfigureAwait(false);
        LinkedFolders.RefuseAny(repo.WorkTree, plan.Touched, ct);
        await RequireNothingInTheWayAsync(repo, from, to, force, note, ct).ConfigureAwait(false);
        await RefuseFileFolderClashAsync(repo, plan, force, ct).ConfigureAwait(false);
        var sizes = await ObjectSizesAsync(repo, plan.Changes.SelectMany(change => new[] { change.OldId, change.NewId }), ct).ConfigureAwait(false);
        EnsureCheckoutSpace(repo, plan.Changes, sizes);

        using var scope = BeginWrite(repo);
        SwitchJournal.Write(repo.GitDir, note?.FromCommit ?? from, to, isBranch ? target : null);

        // git must see nothing to do for the files the patterns name: their index entries are made to say what the target says, which
        // changes no file. The note is already written, so a stop from here on is a stopped switch like any other; a refusal puts back
        // the index it started from, and the note this call wrote goes.
        using var aligned = await AlignIndexAsync(repo, to, plan, force, ownsNote: note is null, ct).ConfigureAwait(false);

        var reporter = progress is null ? null : new ProgressReporter(progress, new CheckoutBytePlan(plan.Changes, sizes));

        await RunCheckoutAsync(repo, target, to, isBranch, force, ownsNote: note is null, aligned, reporter, ct).ConfigureAwait(false);
        reporter?.Complete();
        if (force)
        {
            await SetSkipWorktreeAsync(repo, on: false, plan.NamedInTarget, ct).ConfigureAwait(false);
        }

        // A switch only re-syncs what it was meant to change; any other difference is the player's work and stays for status to report.
        await VerifyAsync(repo, to, plan.Gone, force ? null : plan.Touched, plan.Named, ct).ConfigureAwait(false);

        // git prunes a folder when its last file leaves, so the target's empty folders are made again; this runs before the note goes, so a stop here is finished by repeating the switch.
        await RestoreEmptyFoldersAsync(repo, to, ct).ConfigureAwait(false);
        SwitchJournal.Clear(repo.GitDir);
    }

    private void EnsureCheckoutSpace(RepoRef repo, IReadOnlyList<TreeChange> changes, Dictionary<string, long> sizes)
    {
        var growth = changes.Sum(change => sizes.GetValueOrDefault(change.NewId) - sizes.GetValueOrDefault(change.OldId));
        var required = _options.MinFreeBytesForCheckout + Math.Max(0, growth);

        var free = _freeSpace(repo.WorkTree);
        if (free < required)
        {
            throw new DiskSpaceException(repo.WorkTree, required, free);
        }
    }

    // Runs the switch itself. git refusing (local changes, a lock) leaves nothing changed; any other failure leaves the note that the switch is unfinished.
    private async Task RunCheckoutAsync(RepoRef repo, string target, string to, bool isBranch, bool force, bool ownsNote, AlignedIndex? aligned, ProgressReporter? reporter, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            var result = await RunRawAsync(CheckoutInvocation(repo, target, isBranch, force, reporter), ct).ConfigureAwait(false);
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

                if (await TryRestoreAsync(repo, aligned).ConfigureAwait(false))
                {
                    ClearNoteIf(ownsNote, repo);
                }

                throw new GitOperationException(GitFailure.LockFilePath(stderr), $"git could not take its lock: {stderr.Trim()}");
            }

            if (GitFailure.RefusedToOverwrite(stderr))
            {
                if (await TryRestoreAsync(repo, aligned).ConfigureAwait(false))
                {
                    ClearNoteIf(ownsNote, repo);
                }

                throw RefusalFor(repo, stderr);
            }

            if (GitFailure.IsDiskFull(stderr))
            {
                throw new DiskSpaceException(repo.WorkTree, _options.MinFreeBytesForCheckout, _freeSpace(repo.WorkTree));
            }

            // A file that could not be replaced fails the command after HEAD has moved; the verify pass deals with it.
            if (!HeadIsAt(repo, target, to, isBranch))
            {
                throw GitFailure.ToException(new GitCommandException(["checkout", target], result.ExitCode, stderr));
            }

            return;
        }
    }

    private GitInvocation CheckoutInvocation(RepoRef repo, string target, bool isBranch, bool force, ProgressReporter? reporter)
    {
        List<string> arguments = ["checkout"];
        if (force)
        {
            arguments.Add("--force");
        }

        if (reporter is not null)
        {
            arguments.Add("--progress");
        }

        if (!isBranch)
        {
            arguments.Add("--detach");
        }

        arguments.Add(target);
        arguments.Add("--");

        // Every exit code is allowed through so that stderr can tell a refusal from a half-done switch.
        var invocation = Invocation(repo, readOnly: false, [.. arguments]) with { AllowedExitCodes = _anyExitCode };

        // git holds its file meter back for two seconds; without this a switch that finishes sooner would never report anything.
        return reporter is null
            ? invocation
            : invocation with { OnStderrLine = line => ObserveCheckoutLine(reporter, line), ExtraEnvironment = new Dictionary<string, string?> { ["GIT_PROGRESS_DELAY"] = "0" } };
    }

    private static void ObserveCheckoutLine(ProgressReporter reporter, string line)
    {
        if (CheckoutMeter().Match(line) is { Success: true } meter)
        {
            reporter.Observe(int.Parse(meter.Groups["done"].Value, System.Globalization.CultureInfo.InvariantCulture), int.Parse(meter.Groups["total"].Value, System.Globalization.CultureInfo.InvariantCulture));
        }
    }

    // git refused because local changes (or new files) are in the way: a file that cannot be read is named as such, anything else is the player's work.
    private static SavepointException RefusalFor(RepoRef repo, string stderr)
    {
        foreach (var path in GitFailure.OverwrittenPaths(stderr))
        {
            var full = Path.Combine(repo.WorkTree, path.Replace('/', Path.DirectorySeparatorChar));
            if (FileLockProbe.Probe(full) is { } kind)
            {
                return new HeldFile(full, kind).ToException(HeldFileOperation.Switch);
            }
        }

        return new DirtyWorkingTreeException(repo.Role);
    }

    private static bool HeadIsAt(RepoRef repo, string target, string to, bool isBranch)
    {
        var head = GitDirFiles.ReadHead(repo.GitDir);
        return isBranch ? string.Equals(head.Branch, target, StringComparison.Ordinal) : string.Equals(head.DetachedCommit, to, StringComparison.Ordinal);
    }

    // A note describes a switch; one this call did not start describes work that is still unfinished, and a refusal changes nothing in it.
    private static void ClearNoteIf(bool ownsNote, RepoRef repo)
    {
        if (ownsNote)
        {
            SwitchJournal.Clear(repo.GitDir);
        }
    }

    // Up to three passes: find what does not match the point, give it to git again, wait a moment, look again. With a
    // list of paths in <paramref name="only"/> nothing else is touched, whatever differs, and the paths in <paramref name="skip"/> are
    // never touched.
    private async Task VerifyAsync(RepoRef repo, string to, IReadOnlyList<GoneFile> expectedGone, IReadOnlySet<string>? only, IReadOnlySet<string> skip, CancellationToken ct)
    {
        for (var pass = 0; ; pass++)
        {
            ct.ThrowIfCancellationRequested();
            var differing = (await RunAsync(Invocation(repo, readOnly: true, "diff", "--name-only", "-z", "--no-renames", to, "--"), ct).ConfigureAwait(false))
                .SplitStandardOutputOnNul()
                .Where(path => (only is null || only.Contains(path)) && !skip.Contains(path))
                .ToArray();
            var leftovers = expectedGone.Where(gone => IsLeftover(repo, gone)).Select(gone => gone.Path).ToArray();
            if (differing.Length == 0 && leftovers.Length == 0)
            {
                return;
            }

            if (pass >= _options.ResyncDelays.Count)
            {
                var first = FullPath(repo, differing.Length > 0 ? differing[0] : leftovers[0]);
                throw new GitOperationException(first, NotUpdatedMessage([.. differing, .. leftovers]));
            }

            await ResyncAsync(repo, to, differing, leftovers, ct).ConfigureAwait(false);
            await Task.Delay(_options.ResyncDelays[pass], ct).ConfigureAwait(false);
        }
    }

    // A file the point being left held and the target does not is still there. It is ours to remove only when it is
    // exactly the file the point held: under that very name (not one that differs in case), not behind a folder link,
    // with the bytes the point stored. Anything else is the player's own file, which a point never recorded or has since
    // been replaced, and stays.
    private static bool IsLeftover(RepoRef repo, GoneFile gone)
    {
        var full = FullPath(repo, gone.Path);
        if (!File.Exists(full) || LinkedFolders.IsInsideLink(repo.WorkTree, gone.Path) || !HasExactName(full))
        {
            return false;
        }

        // A file that cannot be read cannot be compared; it is reported as in use rather than quietly kept.
        return BlobHasher.TryHash(full, out var id) switch
        {
            HashOutcome.Hashed => string.Equals(id, gone.OldId, StringComparison.Ordinal),
            HashOutcome.Vanished => false,
            _ => true,
        };
    }

    // File.Exists ignores case on Windows and macOS; the folder's own listing says how the name is really spelled.
    private static bool HasExactName(string full)
    {
        var name = Path.GetFileName(full);
        return Directory.EnumerateFileSystemEntries(Path.GetDirectoryName(full)!).Any(entry => string.Equals(Path.GetFileName(entry), name, StringComparison.Ordinal));
    }

    // The paths go to git on stdin: a long list does not fit on a Windows command line. A failure here shows up as the next pass finding the path again.
    private async Task ResyncAsync(RepoRef repo, string to, IReadOnlyList<string> differing, IReadOnlyList<string> leftovers, CancellationToken ct)
    {
        foreach (var path in leftovers)
        {
            TryDelete(FullPath(repo, path));
        }

        if (differing.Count > 0)
        {
            var invocation = Invocation(repo, readOnly: false, "checkout", to).WithPathspecs(differing) with { AllowedExitCodes = _anyExitCode };
            await RunRawAsync(invocation, ct).ConfigureAwait(false);
        }
    }

    private static string FullPath(RepoRef repo, string relative) => Path.Combine(repo.WorkTree, relative.Replace('/', Path.DirectorySeparatorChar));

    private static string NotUpdatedMessage(IReadOnlyList<string> paths)
    {
        var named = string.Join(", ", paths.Take(_namedPathsInMessage));
        return paths.Count > _namedPathsInMessage
            ? $"Checkout could not update: {named} and {paths.Count - _namedPathsInMessage} more."
            : $"Checkout could not update: {named}";
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Still in use; the next pass reports it if it is still there.
        }
    }

    private async Task<string?> ResolveCommitAsync(RepoRef repo, string commitId, CancellationToken ct)
    {
        var result = await RunAsync(GitDirOnly(repo, "rev-parse", "--verify", "-q", commitId + "^{commit}") with { AllowedExitCodes = [0, 1] }, ct).ConfigureAwait(false);
        return result.ExitCode == 0 ? result.StandardOutput.Trim() : null;
    }

    [GeneratedRegex(@"Updating files:\s+\d+%\s+\((?<done>\d+)/(?<total>\d+)\)")]
    private static partial Regex CheckoutMeter();
}
