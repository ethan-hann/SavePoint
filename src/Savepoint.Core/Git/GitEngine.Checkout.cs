using System.Text.RegularExpressions;
using Savepoint.Core.Errors;

namespace Savepoint.Core.Git;

public sealed partial class GitEngine
{
    private const int _namedPathsInMessage = 20;

    private static readonly int[] _anyExitCode = [.. Enumerable.Range(0, 256)];

    /// <summary>
    /// Switches the folder to a branch: git rewrites the files that differ, then the engine verifies that the tree
    /// matches the point and re-syncs any path that stayed behind (a file another program held open). Local changes to
    /// files that differ between the two points make git refuse; other local changes are carried over, so the caller
    /// checks <see cref="GetStatusAsync"/> first.
    /// </summary>
    /// <param name="repo">The folder to switch.</param>
    /// <param name="branch">The profile's branch; it must have at least one point.</param>
    /// <param name="progress">Receives git's own file counts; may be null.</param>
    /// <param name="ct">Cancels the switch. git and its children are stopped and the switch is reported as unfinished until repeated or discarded.</param>
    /// <exception cref="Savepoint.Core.Errors.PathSafetyException">The work tree is a protected location.</exception>
    /// <exception cref="Savepoint.Core.Errors.ConsentMarkerException">The folder has no marker for this game and role.</exception>
    /// <exception cref="Savepoint.Core.Errors.DiskSpaceException">The work tree's volume cannot take the files the switch adds; nothing was written.</exception>
    /// <exception cref="Savepoint.Core.Errors.DirtyWorkingTreeException">Local changes to files the switch would replace; nothing was changed.</exception>
    /// <exception cref="Savepoint.Core.Errors.ProfileHasNoPointsException">The branch has no point to switch to.</exception>
    /// <exception cref="Savepoint.Core.Errors.GitOperationException">A file stayed in use through every attempt (the path is in its arguments), or git failed.</exception>
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
    /// Switches the folder to a branch and makes it match, discarding local changes to tracked files and overwriting new
    /// files that are in the way. Everything else behaves like <see cref="CheckoutBranchAsync"/>.
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
    /// Throws away every change since the checked-out point: modified and deleted files come back, new files that are
    /// that the game's ignore patterns do not name are removed (an ignore file inside the game folder protects nothing).
    /// Files those patterns name and the consent marker stay. Works on a detached HEAD, and also finishes
    /// the job of an unfinished switch by returning the folder to the point HEAD is on (which is the point the switch
    /// started from unless HEAD had already moved; <see cref="RepoStatus.InterruptedSwitch"/> says which).
    /// </summary>
    /// <param name="repo">The folder.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <exception cref="Savepoint.Core.Errors.GitOperationException">The repository has no point to return to, or a file stayed in use through every attempt.</exception>
    public async Task DiscardChangesAsync(RepoRef repo, CancellationToken ct = default)
    {
        repo = Normalize(repo);
        ct.ThrowIfCancellationRequested();
        CheckWorkTree(repo);
        RepoMarker.Verify(repo);
        if (!IsInitialized(repo.GitDir))
        {
            throw new GitOperationException(null, $"The repository at '{repo.GitDir}' does not exist.");
        }

        var point = await RequireSavedPointAsync(repo, ct).ConfigureAwait(false);
        await RefuseLinkedFoldersInChangesAsync(repo, includeChanged: true, ct).ConfigureAwait(false);

        using var scope = BeginWrite(repo);

        // A file another program holds fails the reset for that file only; the verify pass below finds it and tries again.
        var reset = await RunRawAsync(Invocation(repo, readOnly: false, "reset", "--hard", "--quiet") with { AllowedExitCodes = _anyExitCode }, ct).ConfigureAwait(false);
        if (reset.ExitCode != 0 && !reset.StandardError.Contains("unable to", StringComparison.Ordinal))
        {
            throw GitFailure.ToException(new GitCommandException(["reset", "--hard"], reset.ExitCode, reset.StandardError));
        }

        await VerifyAsync(repo, point, [], null, ct).ConfigureAwait(false);
        await RemoveNewFilesAsync(repo, ct).ConfigureAwait(false);
        SwitchJournal.Clear(repo.GitDir);
    }

    /// <summary>
    /// Removes new files and folders; changes to recorded files stay. Files the game's ignore patterns name (as of the last
    /// capture or preview), the consent marker and folders that keep their own history stay. An ignore file inside the
    /// game folder protects nothing.
    /// </summary>
    /// <param name="repo">The folder.</param>
    /// <param name="ct">Cancels the call.</param>
    public async Task CleanAsync(RepoRef repo, CancellationToken ct = default)
    {
        repo = Normalize(repo);
        ct.ThrowIfCancellationRequested();
        CheckWorkTree(repo);
        RepoMarker.Verify(repo);
        if (!IsInitialized(repo.GitDir))
        {
            throw new GitOperationException(null, $"The repository at '{repo.GitDir}' does not exist.");
        }

        // Before the first point every file in the folder is "new"; cleaning would delete the player's whole folder.
        await RequireSavedPointAsync(repo, ct).ConfigureAwait(false);
        await RefuseLinkedFoldersInChangesAsync(repo, includeChanged: false, ct).ConfigureAwait(false);

        using var scope = BeginWrite(repo);
        await RemoveNewFilesAsync(repo, ct).ConfigureAwait(false);
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
            throw new GitOperationException(null, $"The repository at '{repo.GitDir}' does not exist.");
        }

        var head = GitDirFiles.ReadHead(repo.GitDir);
        var from = head.IsDetached ? head.DetachedCommit : await ResolveBranchAsync(repo, head.Branch!, ct).ConfigureAwait(false);
        var to = isBranch ? await ResolveBranchAsync(repo, target, ct).ConfigureAwait(false) : await ResolveCommitAsync(repo, target, ct).ConfigureAwait(false);
        if (to is null)
        {
            throw isBranch
                ? new ProfileHasNoPointsException(target)
                : new GitOperationException(null, $"The point '{target}' is not in the repository.");
        }

        // Which files the switch will write and remove is known before it starts: nothing unsaved may be in the way, and a
        // file that cannot be removed is found afterwards.
        var note = SwitchJournal.Read(repo.GitDir);
        var plan = await PlanSwitchAsync(repo, from, to, note, ct).ConfigureAwait(false);
        LinkedFolders.RefuseAny(repo.WorkTree, plan.Touched, ct);
        await RequireNothingInTheWayAsync(repo, from, to, force, note, plan, ct).ConfigureAwait(false);
        await EnsureCheckoutSpaceAsync(repo, plan.Changes, ct).ConfigureAwait(false);

        using var scope = BeginWrite(repo);
        SwitchJournal.Write(repo.GitDir, note?.FromCommit ?? from, to);
        var reporter = progress is null ? null : new ProgressReporter(progress);

        await RunCheckoutAsync(repo, target, to, isBranch, force, ownsNote: note is null, reporter, ct).ConfigureAwait(false);
        reporter?.Complete();

        // A switch only re-syncs what it was meant to change; any other difference is the player's work and stays for status to report.
        await VerifyAsync(repo, to, plan.Gone, force ? null : plan.Touched, ct).ConfigureAwait(false);
        SwitchJournal.Clear(repo.GitDir);
    }

    private async Task EnsureCheckoutSpaceAsync(RepoRef repo, IReadOnlyList<TreeChange> changes, CancellationToken ct)
    {
        var sizes = await ObjectSizesAsync(repo, changes.SelectMany(change => new[] { change.OldId, change.NewId }), ct).ConfigureAwait(false);
        var growth = changes.Sum(change => sizes.GetValueOrDefault(change.NewId) - sizes.GetValueOrDefault(change.OldId));
        var required = _options.MinFreeBytesForCheckout + Math.Max(0, growth);

        var free = _freeSpace(repo.WorkTree);
        if (free < required)
        {
            throw new DiskSpaceException(repo.WorkTree, required, free);
        }
    }

    // Runs the switch itself. git refusing (local changes, a lock) leaves nothing changed; any other failure leaves the note that the switch is unfinished.
    private async Task RunCheckoutAsync(RepoRef repo, string target, string to, bool isBranch, bool force, bool ownsNote, ProgressReporter? reporter, CancellationToken ct)
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

                ClearNoteIf(ownsNote, repo);
                throw new GitOperationException(GitFailure.LockFilePath(stderr), $"git could not take its lock: {stderr.Trim()}");
            }

            if (GitFailure.RefusedToOverwrite(stderr))
            {
                ClearNoteIf(ownsNote, repo);
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
        return reporter is null ? invocation : invocation with { OnStderrLine = line => ObserveCheckoutLine(reporter, line) };
    }

    private static void ObserveCheckoutLine(ProgressReporter reporter, string line)
    {
        if (CheckoutMeter().Match(line) is { Success: true } meter)
        {
            reporter.Observe(int.Parse(meter.Groups["done"].Value, System.Globalization.CultureInfo.InvariantCulture), int.Parse(meter.Groups["total"].Value, System.Globalization.CultureInfo.InvariantCulture));
        }
    }

    // git refused because local changes (or new files) are in the way: a file another program holds is "in use", anything else is the player's work.
    private static SavepointException RefusalFor(RepoRef repo, string stderr)
    {
        foreach (var path in GitFailure.OverwrittenPaths(stderr))
        {
            var full = Path.Combine(repo.WorkTree, path.Replace('/', Path.DirectorySeparatorChar));
            if (FileLockProbe.IsLocked(full))
            {
                return new GitOperationException(full, $"'{full}' is in use by another program, so the folder could not be switched.");
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
    // list of paths in <paramref name="only"/> nothing else is touched, whatever differs.
    private async Task VerifyAsync(RepoRef repo, string to, IReadOnlyList<GoneFile> expectedGone, IReadOnlySet<string>? only, CancellationToken ct)
    {
        for (var pass = 0; ; pass++)
        {
            ct.ThrowIfCancellationRequested();
            var differing = (await RunAsync(Invocation(repo, readOnly: true, "diff", "--name-only", "-z", "--no-renames", to, "--"), ct).ConfigureAwait(false))
                .SplitStandardOutputOnNul()
                .Where(path => only is null || only.Contains(path))
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
