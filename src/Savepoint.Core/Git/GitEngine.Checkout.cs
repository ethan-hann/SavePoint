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
    /// A switch that finishes a stopped one (the same target) writes the target's copy of the files the stopped switch was writing, whatever
    /// they hold now, because a stopped switch leaves some of them half written (a file that already holds the target's bytes is left as it is);
    /// <see cref="ReviewStoppedSwitchAsync"/> says which hold neither snapshot's copy. An unsaved change in any other file, or an edit to a file
    /// the switch removes, still refuses it before anything is written.
    /// When the switch is done it creates the empty folders the target point was taken with (git does not track them) that are missing.
    /// It only creates: no folder is removed or filled, and one the game's patterns name, one below a folder link and one a file of the
    /// player's stands on are left out. An empty folder the target does not hold is left as it is. This holds for the other switch methods too.
    /// </summary>
    /// <param name="repo">The folder to switch.</param>
    /// <param name="branch">The profile's branch; it must have at least one point.</param>
    /// <param name="progress">Receives git's own file counts; may be null.</param>
    /// <param name="ct">Cancels the switch. git and its children are stopped and the switch is reported as unfinished until repeated or discarded.</param>
    /// <exception cref="Savepoint.Core.Errors.PathSafetyException">The work tree is a protected location.</exception>
    /// <exception cref="Savepoint.Core.Errors.ConsentMarkerException">The folder has no marker for this game and role.</exception>
    /// <exception cref="Savepoint.Core.Errors.FolderChangedException">The folder was replaced, moved or re-linked after it was approved; the step that would have changed files was not run (steps that finished before it are not undone). A marker that goes missing or changes in that time raises <see cref="Savepoint.Core.Errors.ConsentMarkerException"/> instead, and one that cannot be read, <see cref="Savepoint.Core.Errors.GitOperationException"/>.</exception>
    /// <exception cref="Savepoint.Core.Errors.PathMissingException">The work tree does not exist.</exception>
    /// <exception cref="Savepoint.Core.Errors.DiskSpaceException">The work tree's volume cannot take the files the switch adds (nothing was written), or the disk filled during the switch (it is then left unfinished; see <see cref="RepoStatus.InterruptedSwitch"/>).</exception>
    /// <exception cref="Savepoint.Core.Errors.DirtyWorkingTreeException">Unsaved changes, or a file the game's patterns name, are in the way; nothing was changed.</exception>
    /// <exception cref="Savepoint.Core.Errors.InterruptedSwitchException">A stopped switch left the folder half done and this one is not the switch to finish it; nothing was changed.</exception>
    /// <exception cref="Savepoint.Core.Errors.LinkedFolderException">On Windows, a folder link lies in the way; nothing was changed.</exception>
    /// <exception cref="Savepoint.Core.Errors.LinkedFileException">On Windows, a file the switch would write or remove is a symbolic link; nothing was changed.</exception>
    /// <exception cref="Savepoint.Core.Errors.FileFolderClashException">Writing a file would delete a folder holding files of the player's that the switch does not remove, or making a folder would delete a file of the player's; nothing was changed.</exception>
    /// <exception cref="Savepoint.Core.Errors.NamesDifferOnlyInCaseException">The folder is on a drive that ignores case and the target point holds two names that differ only in case; nothing was changed.</exception>
    /// <exception cref="Savepoint.Core.Errors.ProfileHasNoPointsException">The branch has no point to switch to.</exception>
    /// <exception cref="Savepoint.Core.Errors.GitOperationException">A file stayed in use through every attempt (the path is in its arguments), the only files reported as changed are held open by another program so they could not be read, or git failed.</exception>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    public Task CheckoutBranchAsync(RepoRef repo, string branch, IProgress<FileProgress>? progress = null, CancellationToken ct = default)
    {
        BranchNames.Require(branch);
        return CheckoutAsync(repo, branch, isBranch: true, force: false, progress, ct);
    }

    /// <summary>
    /// Switches the folder to an older point, leaving HEAD detached. Behaves like <see cref="CheckoutBranchAsync"/> otherwise, including
    /// creating the empty folders that point was taken with.
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
    /// else behaves like <see cref="CheckoutBranchAsync"/>, including creating the empty folders the target point was taken with.
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
    /// Switches the folder to an older point and makes it match, discarding local changes like <see cref="ForceCheckoutBranchAsync"/>, and
    /// creates the empty folders that point was taken with.
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
    /// <exception cref="Savepoint.Core.Errors.FolderChangedException">The folder was replaced, moved or re-linked after it was approved; the step that would have changed files was not run (steps that finished before it are not undone). A marker that goes missing or changes in that time raises <see cref="Savepoint.Core.Errors.ConsentMarkerException"/> instead, and one that cannot be read, <see cref="Savepoint.Core.Errors.GitOperationException"/>.</exception>
    /// <exception cref="Savepoint.Core.Errors.ProfileHasNoPointsException">The folder has no saved point to return to; without one a discard would delete the whole folder, so nothing is touched.</exception>
    /// <exception cref="Savepoint.Core.Errors.LinkedFolderException">On Windows, a folder link lies in the way; nothing was changed.</exception>
    /// <exception cref="Savepoint.Core.Errors.FileFolderClashException">Restoring a file would delete a folder holding files of the player's that the discard does not remove, or restoring a folder would delete a file of the player's that the patterns name; nothing was changed.</exception>
    /// <exception cref="Savepoint.Core.Errors.NamesDifferOnlyInCaseException">The folder is on a drive that ignores case and the point holds two names that differ only in case; nothing was changed.</exception>
    /// <exception cref="Savepoint.Core.Errors.DiskSpaceException">The disk filled while files were restored.</exception>
    /// <exception cref="Savepoint.Core.Errors.SwitchNoteStuckException">The folder was put back but the note of an unfinished switch could not be removed (another program holds it open); discarding again removes it.</exception>
    /// <exception cref="Savepoint.Core.Errors.GitOperationException">A file stayed in use through every attempt, or git failed.</exception>
    /// <remarks>A point taken since empty folders were recorded holds its own, and a discard makes them again (git does not track them). A discard removes the empty folders the point does not hold along with the new files, and every empty folder of a point taken before that.</remarks>
    public async Task DiscardChangesAsync(RepoRef repo, CancellationToken ct = default)
    {
        repo = Normalize(repo);
        ct.ThrowIfCancellationRequested();
        using var guard = Approve(repo);
        repo = repo with { Guard = guard };
        if (!IsInitialized(repo.GitDir))
        {
            throw new GitOperationException($"The repository at '{repo.GitDir}' does not exist.");
        }

        using var scope = BeginWrite(repo);

        // A mark a killed switch left behind would keep the discard from seeing, and restoring, the file.
        await ClearSkipWorktreeAsync(repo, ct).ConfigureAwait(false);

        var point = await RequireSavedPointAsync(repo, ct).ConfigureAwait(false);
        await RefuseLinkedFoldersInChangesAsync(repo, includeChanged: true, ct).ConfigureAwait(false);
        await RefuseCaseClashAsync(repo, point, ct).ConfigureAwait(false);
        await RefuseDiscardClashAsync(repo, ct).ConfigureAwait(false);

        var named = await ResetToPointAsync(repo, ct).ConfigureAwait(false);
        await RefreshIndexAsync(repo, ct).ConfigureAwait(false);
        await VerifyAsync(repo, point, [], null, named, ct).ConfigureAwait(false);
        await RemoveNewFilesAsync(repo, ct).ConfigureAwait(false);
        await RestoreEmptyFoldersAsync(repo, point, ct).ConfigureAwait(false);

        // The files are back; a note that stays would keep the folder looking half switched, and the player would be sent round again.
        if (!await ClearNoteAsync(repo).ConfigureAwait(false))
        {
            throw new SwitchNoteStuckException(repo.Role, SwitchJournal.PathIn(repo.GitDir));
        }
    }

    // Writes the point's copy of every file that differs and returns the files the patterns name, which are left as they are. With no
    // patterns that is one hard reset. With patterns the index goes back to the point first, which changes no file, and only the
    // differing files they do not name are written: a hard reset would write the point's copy of a named file as well.
    private async Task<IReadOnlySet<string>> ResetToPointAsync(RepoRef repo, CancellationToken ct)
    {
        if (!HasPatterns(repo))
        {
            await RunRewriteAsync(repo, Invocation(repo, readOnly: false, "reset", "--hard", "--quiet"), ["reset", "--hard"], ct).ConfigureAwait(false);
            return new HashSet<string>();
        }

        await RunWriteAsync(() => Invocation(repo, readOnly: false, "reset", "--quiet"), ct).ConfigureAwait(false);
        var named = await RecordedNamedAsync(repo, ct).ConfigureAwait(false);
        var differing = (await DiffNamesAsync(repo, "HEAD", ct).ConfigureAwait(false)).Where(path => !named.Contains(path)).ToArray();
        if (differing.Length > 0)
        {
            await RunRewriteAsync(repo, Invocation(repo, readOnly: false, "checkout", "HEAD").WithPathspecs(differing), ["checkout", "HEAD"], ct).ConfigureAwait(false);
        }

        return named;
    }

    // A file git just wrote has a time stamp at the same moment as the index (to the resolution git compares), so git cannot trust the stamp and reads the whole file
    // again on every later comparison (a status, a dirty check, the next switch) until the index is rewritten. Nothing else rewrites
    // it, because every read runs without taking git's optional locks. One pass here reads each such file once and ends that. A
    // failure only costs time: the verify pass that follows reads what this could not.
    private Task RefreshIndexAsync(RepoRef repo, CancellationToken ct) =>
        RunRawAsync(Invocation(repo, readOnly: false, "update-index", "-q", "--refresh") with { AllowedExitCodes = _anyExitCode }, ct);

    // A file another program holds fails the command for that file only; the verify pass after it finds the file and tries again.
    private async Task RunRewriteAsync(RepoRef repo, GitInvocation invocation, string[] command, CancellationToken ct)
    {
        var result = await RunRawAsync(invocation with { AllowedExitCodes = _anyExitCode }, ct).ConfigureAwait(false);
        if (result.ExitCode == 0)
        {
            return;
        }

        if (GitFailure.IsDiskFull(result.StandardError))
        {
            throw new DiskSpaceException(repo.WorkTree, _options.MinFreeBytesForCheckout, _freeSpace(repo.WorkTree));
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
    /// <exception cref="Savepoint.Core.Errors.FolderChangedException">The folder was replaced, moved or re-linked after it was approved; the step that would have changed files was not run. A marker that goes missing or changes in that time raises <see cref="Savepoint.Core.Errors.ConsentMarkerException"/> instead, and one that cannot be read, <see cref="Savepoint.Core.Errors.GitOperationException"/>.</exception>
    /// <exception cref="Savepoint.Core.Errors.ProfileHasNoPointsException">The folder has no saved point, so every file in it would count as new and be removed; nothing is touched.</exception>
    /// <exception cref="Savepoint.Core.Errors.LinkedFolderException">On Windows, a folder link lies in the way; nothing was changed.</exception>
    /// <exception cref="Savepoint.Core.Errors.GitOperationException">A file stayed in use through every attempt, or git failed.</exception>
    public async Task CleanAsync(RepoRef repo, CancellationToken ct = default)
    {
        repo = Normalize(repo);
        ct.ThrowIfCancellationRequested();
        using var guard = Approve(repo);
        repo = repo with { Guard = guard };
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
        LinkedFolders.RefuseAny(repo.WorkTree, changed.Concat((await untracked.ConfigureAwait(false)).Files), ct, includeFileLinks: false);
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
                throw new GitOperationException(FullPath(repo, left[0]), NotUpdatedMessage(FileStep.RemoveNew, left));
            }

            RecheckBeforeFileChanges(repo, "remove-new");
            foreach (var path in left)
            {
                TryDelete(RealFullPath(repo, path));
            }

            await Task.Delay(_options.ResyncDelays[pass], ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Asks, without writing anything, whether a switch of the folder to a branch or an older point would be turned away before it writes
    /// a file: free space on the folder's volume, names that differ only in case, a folder link on Windows, a file and a folder that cannot
    /// share a path, unsaved changes and a half-switched folder. The folder, the repository's index and config, its refs and the note of an
    /// unfinished switch are only read. When the game's patterns name files, git is also given a throwaway <c>savepoint-classify-*</c> index
    /// in the git directory, which is deleted when the question is answered. A launch that switches several folders asks the later ones
    /// first, so a refusal in a later folder leaves the earlier ones as they were.
    /// </summary>
    /// <param name="repo">The folder that would be switched.</param>
    /// <param name="target">The branch name, or the full id of an older point.</param>
    /// <param name="isBranch">Whether <paramref name="target"/> is a branch.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <remarks>A switch that passes can still fail later: a file another program holds open, or a disk that fills meanwhile.</remarks>
    /// <exception cref="Savepoint.Core.Errors.PathSafetyException">The work tree is a protected location.</exception>
    /// <exception cref="Savepoint.Core.Errors.PathMissingException">The work tree does not exist.</exception>
    /// <exception cref="Savepoint.Core.Errors.ConsentMarkerException">The folder has no marker for this game and role.</exception>
    /// <exception cref="Savepoint.Core.Errors.DiskSpaceException">The work tree's volume cannot take the files the switch adds.</exception>
    /// <exception cref="Savepoint.Core.Errors.DirtyWorkingTreeException">Unsaved changes, or a file the game's patterns name, are in the way.</exception>
    /// <exception cref="Savepoint.Core.Errors.InterruptedSwitchException">A stopped switch left the folder half done and this is not the switch to finish it.</exception>
    /// <exception cref="Savepoint.Core.Errors.LinkedFolderException">On Windows, a folder link lies in the way.</exception>
    /// <exception cref="Savepoint.Core.Errors.LinkedFileException">On Windows, a file the switch would write or remove is a symbolic link.</exception>
    /// <exception cref="Savepoint.Core.Errors.FileFolderClashException">Writing a file would delete a folder holding files of the player's, or making a folder would delete a file of the player's.</exception>
    /// <exception cref="Savepoint.Core.Errors.NamesDifferOnlyInCaseException">The folder is on a drive that ignores case and the target holds two names that differ only in case.</exception>
    /// <exception cref="Savepoint.Core.Errors.ProfileHasNoPointsException">The branch has no point to switch to.</exception>
    /// <exception cref="ArgumentException"><paramref name="target"/> is not a valid branch name, or not a full commit id.</exception>
    /// <exception cref="Savepoint.Core.Errors.GitOperationException">The repository does not exist, the point is not in it, or git failed.</exception>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    public async Task CheckSwitchAsync(RepoRef repo, string target, bool isBranch, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(target);
        if (isBranch)
        {
            BranchNames.Require(target);
        }
        else
        {
            RequireCommitId(target);
        }

        repo = Normalize(repo);
        ct.ThrowIfCancellationRequested();

        // The folder is not pinned or held: nothing is written to it here, and the switch itself looks again.
        CheckWorkTree(repo);
        RepoMarker.Verify(repo);
        if (!IsInitialized(repo.GitDir))
        {
            throw new GitOperationException($"The repository at '{repo.GitDir}' does not exist.");
        }

        var (from, to, note) = await ReadSwitchEndsAsync(repo, target, isBranch, ct).ConfigureAwait(false);
        await RequireSwitchPossibleAsync(repo, from, to, force: false, note, readFiles: false, ct).ConfigureAwait(false);
    }

    private async Task CheckoutAsync(RepoRef repo, string target, bool isBranch, bool force, IProgress<FileProgress>? progress, CancellationToken ct)
    {
        repo = Normalize(repo);
        ct.ThrowIfCancellationRequested();
        using var guard = Approve(repo);
        repo = repo with { Guard = guard };
        if (!IsInitialized(repo.GitDir))
        {
            throw new GitOperationException($"The repository at '{repo.GitDir}' does not exist.");
        }

        var (from, to, note) = await ReadSwitchEndsAsync(repo, target, isBranch, ct).ConfigureAwait(false);

        using var scope = BeginWrite(repo);

        // A mark an earlier switch could not take off would hide a file from the checks below.
        await ClearSkipWorktreeAsync(repo, ct).ConfigureAwait(false);

        var (plan, sizes, finishing) = await RequireSwitchPossibleAsync(repo, from, to, force, note, readFiles: true, ct).ConfigureAwait(false);

        // From here the switch writes the index itself (the marks, the alignment, the checkout, the re-syncs), and a rebuild from the last
        // snapshot would undo that. Damage found from now on ends the switch with an error and leaves the note; the next operation repairs.
        using var writing = NoRepairWhileWriting(repo.GitDir);

        // A forced checkout writes every file whose stamp differs from its entry, so a recorded left-out file that both points hold alike is
        // marked too: the player's copy of it differs from the entry and would be overwritten. Those entries already say what the target says.
        var alike = force ? await RecordedNamedAsync(repo, ct).ConfigureAwait(false) : [];
        alike.ExceptWith(plan.Named);

        // An entry the target lacks (one an unfinished switch aligned to its own target) has nothing to be protected from, and marking it
        // makes git refuse the merge.
        alike.IntersectWith(await InTreeAsync(repo, to, alike, ct).ConfigureAwait(false));
        var marks = plan.NamedInTarget.Concat(alike).ToArray();
        var skip = alike.Count == 0 ? plan.Named : new HashSet<string>(plan.Named.Concat(alike), StringComparer.Ordinal);

        // With nothing to align the marks go on the real index. That happens before the note is written, so a failure leaves no note.
        var markedDirectly = plan.Named.Count == 0 && marks.Length > 0;
        if (markedDirectly)
        {
            try
            {
                await SetSkipWorktreeAsync(repo, on: true, marks, ct).ConfigureAwait(false);
            }
            catch
            {
                await TryClearSkipWorktreeAsync(repo).ConfigureAwait(false);
                throw;
            }
        }

        SwitchJournal.Write(repo.GitDir, note?.FromCommit ?? from, to, isBranch ? target : null);

        // git must see nothing to do for the files the patterns name: their index entries are made to say what the target says, which
        // changes no file. The note is already written, so a stop from here on is a stopped switch like any other; a refusal puts back
        // the index it started from, and the note this call wrote goes.
        using var aligned = await AlignIndexAsync(repo, to, plan, marks, force, ownsNote: note is null, ct).ConfigureAwait(false);

        var reporter = progress is null ? null : new ProgressReporter(progress, new CheckoutBytePlan(plan.Changes, sizes));

        // The alignment marked the left-out files skip-worktree, which keeps git from reading them in the checkout, the refresh and the
        // verify diff: a file that differs from the target never matches its entry, so each of the three would read it in full.
        var marked = marks.Length > 0;
        try
        {
            await WriteFinishingPathsAsync(repo, to, finishing, aligned, reporter, ct).ConfigureAwait(false);
            await RunCheckoutAsync(repo, target, to, isBranch, force, ownsNote: note is null, aligned, reporter, ct).ConfigureAwait(false);
            reporter?.Complete();

            // Reading the files back takes as long as reading them, which for a big file is long enough to look stuck.
            reporter?.BeginWrapUp();
            await RefreshIndexAsync(repo, ct).ConfigureAwait(false);

            // A switch only re-syncs what it was meant to change; any other difference is the player's work and stays for status to report.
            await VerifyAsync(repo, to, plan.Gone, force ? null : plan.Touched, skip, ct).ConfigureAwait(false);
            if (marks.Length > 0)
            {
                await ClearSkipWorktreeAsync(repo, ct).ConfigureAwait(false);
            }

            marked = false;

            // git prunes a folder when its last file leaves, so the target's empty folders are made again; this runs before the note goes, so a stop here is finished by repeating the switch.
            await RestoreEmptyFoldersAsync(repo, to, ct).ConfigureAwait(false);
            await ClearNoteAsync(repo).ConfigureAwait(false);
            reporter?.Complete();
        }
        finally
        {
            // A switch that fails or is stopped while it reads the files back must not leave the screen saying it still is.
            reporter?.Abandon();

            // A mark left behind would hide the file from every later comparison, including a save made after the patterns change.
            if (marked)
            {
                await TryClearSkipWorktreeAsync(repo).ConfigureAwait(false);
            }
        }
    }

    // Where the folder is, the point it is to be switched to and the note of an unfinished switch, if any.
    private async Task<(string? From, string To, InterruptedSwitch? Note)> ReadSwitchEndsAsync(RepoRef repo, string target, bool isBranch, CancellationToken ct)
    {
        var head = GitDirFiles.ReadHead(repo.GitDir);
        var from = head.IsDetached ? head.DetachedCommit : await ResolveBranchAsync(repo, head.Branch!, ct).ConfigureAwait(false);
        var to = isBranch ? await ResolveBranchAsync(repo, target, ct).ConfigureAwait(false) : await ResolveCommitAsync(repo, target, ct).ConfigureAwait(false);
        if (to is null)
        {
            throw isBranch
                ? new ProfileHasNoPointsException(target)
                : new GitOperationException($"The point '{target}' is not in the repository.");
        }

        // A note whose switch is not known cannot be told apart from the one this would finish, and writing a new note would erase the
        // evidence. A discard clears such a note.
        var note = await ReadNoteAsync(repo, ct).ConfigureAwait(false);
        if (note is { ToCommit: null })
        {
            throw new InterruptedSwitchException(repo.Role);
        }

        return (from, to, note);
    }

    // Which files the switch will write and remove is known before it starts: nothing unsaved may be in the way, and a
    // file that cannot be removed is found afterwards. Reads only, so it also answers for a switch that is not made yet.
    // Finishing is what a stopped switch left, which git alone would refuse as local changes (see WriteFinishingPathsAsync). Only the switch
    // itself needs to know which of those files already hold the target's bytes, which takes reading them: a question that asks whether the
    // switch could be made passes readFiles false.
    private async Task<(SwitchPlan Plan, Dictionary<string, long> Sizes, FinishScan Finishing)> RequireSwitchPossibleAsync(RepoRef repo, string? from, string to, bool force, InterruptedSwitch? note, bool readFiles, CancellationToken ct)
    {
        var plan = await PlanSwitchAsync(repo, from, to, note, ct).ConfigureAwait(false);
        LinkedFolders.RefuseAny(repo.WorkTree, plan.Touched, ct);
        await RefuseCaseClashAsync(repo, to, ct).ConfigureAwait(false);
        var finishing = await RequireNothingInTheWayAsync(repo, from, to, force, plan, note, readFiles, ct).ConfigureAwait(false);
        await RefuseFileFolderClashAsync(repo, plan, force, ct).ConfigureAwait(false);
        var sizes = await ObjectSizesAsync(repo, plan.Changes.SelectMany(change => new[] { change.OldId, change.NewId }), ct).ConfigureAwait(false);
        EnsureCheckoutSpace(repo, plan.Changes, sizes);
        return (plan, sizes, finishing);
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
    // A damaged index is such a failure and is not repaired here (see NoRepairWhileWriting): a repair that threw would also leave the note.
    private async Task RunCheckoutAsync(RepoRef repo, string target, string to, bool isBranch, bool force, bool ownsNote, AlignedIndex? aligned, ProgressReporter? reporter, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            GitResult result;
            try
            {
                result = await RunRawAsync(CheckoutInvocation(repo, target, isBranch, force, reporter), ct).ConfigureAwait(false);
            }
            catch (Exception e) when (e is FolderChangedException or ConsentMarkerException or GitOperationException)
            {
                // Only the checks before git starts can raise these (the folder changed, the marker is gone or unreadable, the git directory
                // was redirected). Nothing was written, so the note this call wrote goes, as for a refusal by git.
                if (await TryRestoreAsync(repo, aligned).ConfigureAwait(false))
                {
                    await ClearNoteIfAsync(ownsNote, repo).ConfigureAwait(false);
                }

                throw;
            }

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
                    await ClearNoteIfAsync(ownsNote, repo).ConfigureAwait(false);
                }

                throw new GitOperationException(GitFailure.LockFilePath(stderr), $"git could not take its lock: {stderr.Trim()}");
            }

            if (GitFailure.RefusedToOverwrite(stderr))
            {
                if (await TryRestoreAsync(repo, aligned).ConfigureAwait(false))
                {
                    await ClearNoteIfAsync(ownsNote, repo).ConfigureAwait(false);
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
    private async Task ClearNoteIfAsync(bool ownsNote, RepoRef repo)
    {
        if (ownsNote)
        {
            await ClearNoteAsync(repo).ConfigureAwait(false);
        }
    }

    // The note is read and cleared with the engine's wait for a program that holds it, which frees the thread while it waits.
    private Task<InterruptedSwitch?> ReadNoteAsync(RepoRef repo, CancellationToken ct) =>
        SwitchJournal.ReadAsync(repo.GitDir, _options.SwitchNoteDelay, ct);

    private Task<bool> ClearNoteAsync(RepoRef repo) =>
        SwitchJournal.ClearAsync(repo.GitDir, _options.SwitchNoteDelay);

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
                throw new GitOperationException(first, NotUpdatedMessage(FileStep.BringToPoint, [.. differing, .. leftovers]));
            }

            await ResyncAsync(repo, to, differing, leftovers, ct).ConfigureAwait(false);
            await Task.Delay(_options.ResyncDelays[pass], ct).ConfigureAwait(false);

            // The files written again are as new as the first ones were; the next look would read them in full.
            if (differing.Length > 0)
            {
                await RefreshIndexAsync(repo, ct).ConfigureAwait(false);
            }
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
        if (leftovers.Count > 0)
        {
            RecheckBeforeFileChanges(repo, "remove-leftover");
        }

        foreach (var path in leftovers)
        {
            TryDelete(RealFullPath(repo, path));
        }

        if (differing.Count > 0)
        {
            var invocation = Invocation(repo, readOnly: false, "checkout", to).WithPathspecs(differing) with { AllowedExitCodes = _anyExitCode };
            await RunRawAsync(invocation, ct).ConfigureAwait(false);
        }
    }

    private static string FullPath(RepoRef repo, string relative) => Path.Combine(repo.WorkTree, relative.Replace('/', Path.DirectorySeparatorChar));

    // The log text for a file step that left files unchanged: what was being done, then the files.
    internal static string NotUpdatedMessage(FileStep step, IReadOnlyList<string> paths)
    {
        var what = step switch
        {
            FileStep.BringToPoint => "Could not write the point's copy of these files",
            FileStep.RemoveNew => "Could not remove these new files",
            FileStep.RemoveDisplaced => "Could not remove these files the point being left held",
            FileStep.RemoveStanding => "Could not remove these files standing where the point being left had a folder",
            _ => throw new ArgumentOutOfRangeException(nameof(step), step, null),
        };
        var named = string.Join(", ", paths.Take(_namedPathsInMessage));
        return paths.Count > _namedPathsInMessage
            ? $"{what}: {named} and {paths.Count - _namedPathsInMessage} more."
            : $"{what}: {named}";
    }

    // Windows refuses to delete a read-only file, where git's own replacing of a path clears the attribute and goes on. Only for a
    // file the caller has already decided is its to remove. A link is left as it is: its attributes are not the file's.
    private static void TryDeleteReadOnly(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            try
            {
                var attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReadOnly) != 0 && (attributes & FileAttributes.ReparsePoint) == 0)
                {
                    File.SetAttributes(path, attributes & ~FileAttributes.ReadOnly);
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // The delete below fails the same way and is reported there.
            }
        }

        TryDelete(path);
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
