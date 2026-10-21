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
    /// not ignored are removed. Ignored files and the consent marker stay. Works on a detached HEAD, and also finishes
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

        var head = GitDirFiles.ReadHead(repo.GitDir);
        var point = head.IsDetached ? head.DetachedCommit : await ResolveBranchAsync(repo, head.Branch!, ct).ConfigureAwait(false);
        if (point is null)
        {
            throw new GitOperationException(null, "The folder has no saved point to return to, so there is nothing to discard back to.");
        }

        using var scope = BeginWrite(repo);

        // A file another program holds fails the reset for that file only; the verify pass below finds it and tries again.
        var reset = await RunRawAsync(Invocation(repo, readOnly: false, "reset", "--hard", "--quiet") with { AllowedExitCodes = _anyExitCode }, ct).ConfigureAwait(false);
        if (reset.ExitCode != 0 && !reset.StandardError.Contains("unable to", StringComparison.Ordinal))
        {
            throw GitFailure.ToException(new GitCommandException(["reset", "--hard"], reset.ExitCode, reset.StandardError));
        }

        await VerifyAsync(repo, point, [], ct).ConfigureAwait(false);
        await RemoveNewFilesAsync(repo, ct).ConfigureAwait(false);
        SwitchJournal.Clear(repo.GitDir);
    }

    /// <summary>Removes new files and folders that are not ignored; changes to tracked files stay. Ignored files and the consent marker stay.</summary>
    /// <param name="repo">The folder.</param>
    /// <param name="ct">Cancels the call.</param>
    public async Task CleanAsync(RepoRef repo, CancellationToken ct = default)
    {
        repo = Normalize(repo);
        ct.ThrowIfCancellationRequested();
        CheckWorkTree(repo);
        RepoMarker.Verify(repo);

        using var scope = BeginWrite(repo);
        await RemoveNewFilesAsync(repo, ct).ConfigureAwait(false);
    }

    // Removes new files that are not ignored; a file still in use is retried and then named.
    private async Task RemoveNewFilesAsync(RepoRef repo, CancellationToken ct)
    {
        await RunRawAsync(Invocation(repo, readOnly: false, "clean", "-f", "-d", "--quiet") with { AllowedExitCodes = _anyExitCode }, ct).ConfigureAwait(false);
        for (var pass = 0; ; pass++)
        {
            ct.ThrowIfCancellationRequested();
            var left = (await RunAsync(Invocation(repo, readOnly: true, "ls-files", "-z", "--others", "--exclude-standard"), ct).ConfigureAwait(false))
                .SplitStandardOutputOnNul();
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
            throw new GitOperationException(null, isBranch ? $"The branch '{target}' has no points to switch to." : $"The point '{target}' is not in the repository.");
        }

        // Which files the switch will remove is known before it starts, so one that cannot be removed is found afterwards.
        var changes = await DiffTreesAsync(repo, from ?? GitDirFiles.EmptyTree, to, ct).ConfigureAwait(false);
        await EnsureCheckoutSpaceAsync(repo, changes, ct).ConfigureAwait(false);
        var expectedGone = await FilesToBeRemovedAsync(repo, from, to, changes, ct).ConfigureAwait(false);

        using var scope = BeginWrite(repo);
        SwitchJournal.Write(repo.GitDir, SwitchJournal.Read(repo.GitDir)?.FromCommit ?? from, to);
        var reporter = progress is null ? null : new ProgressReporter(progress);

        await RunCheckoutAsync(repo, target, to, isBranch, force, reporter, ct).ConfigureAwait(false);
        reporter?.Complete();
        await VerifyAsync(repo, to, expectedGone, ct).ConfigureAwait(false);
        SwitchJournal.Clear(repo.GitDir);
    }

    // What the point being left has and the target does not. After an unfinished switch HEAD may already be at the target,
    // so the point the unfinished switch started from counts too.
    private async Task<string[]> FilesToBeRemovedAsync(RepoRef repo, string? from, string to, IReadOnlyList<TreeChange> changes, CancellationToken ct)
    {
        var gone = new HashSet<string>(changes.Where(change => change.Status == 'D').Select(change => change.Path), StringComparer.Ordinal);
        if (SwitchJournal.Read(repo.GitDir)?.FromCommit is { } earlier && !string.Equals(earlier, from, StringComparison.Ordinal))
        {
            var before = await DiffTreesAsync(repo, earlier, to, ct).ConfigureAwait(false);
            gone.UnionWith(before.Where(change => change.Status == 'D').Select(change => change.Path));
        }

        return [.. gone];
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
    private async Task RunCheckoutAsync(RepoRef repo, string target, string to, bool isBranch, bool force, ProgressReporter? reporter, CancellationToken ct)
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

                SwitchJournal.Clear(repo.GitDir);
                throw new GitOperationException(GitFailure.LockFilePath(stderr), $"git could not take its lock: {stderr.Trim()}");
            }

            if (GitFailure.RefusedToOverwrite(stderr))
            {
                SwitchJournal.Clear(repo.GitDir);
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

    // Up to three passes: find what does not match the point, give it to git again, wait a moment, look again.
    private async Task VerifyAsync(RepoRef repo, string to, IReadOnlyList<string> expectedGone, CancellationToken ct)
    {
        for (var pass = 0; ; pass++)
        {
            ct.ThrowIfCancellationRequested();
            var differing = (await RunAsync(Invocation(repo, readOnly: true, "diff", "--name-only", "-z", "--no-renames", to, "--"), ct).ConfigureAwait(false))
                .SplitStandardOutputOnNul();
            var leftovers = expectedGone.Where(path => File.Exists(FullPath(repo, path))).ToArray();
            if (differing.Count == 0 && leftovers.Length == 0)
            {
                return;
            }

            if (pass >= _options.ResyncDelays.Count)
            {
                var first = FullPath(repo, differing.Count > 0 ? differing[0] : leftovers[0]);
                throw new GitOperationException(first, NotUpdatedMessage([.. differing, .. leftovers]));
            }

            await ResyncAsync(repo, to, differing, leftovers, ct).ConfigureAwait(false);
            await Task.Delay(_options.ResyncDelays[pass], ct).ConfigureAwait(false);
        }
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
