using Savepoint.Core.Errors;

namespace Savepoint.Core.Git;

public sealed partial class GitEngine
{
    /// <summary>
    /// Records the folder's current contents as a point on a branch, as described by <paramref name="request"/>.
    /// Nothing in the work tree is written or switched. Files named by the request's ignore patterns, and the
    /// consent marker, are never recorded.
    /// </summary>
    /// <param name="repo">The folder to capture; its repository must exist.</param>
    /// <param name="request">The branch, message, capture id, mode and ignore patterns.</param>
    /// <param name="progress">Receives files done and expected while git works; may be null.</param>
    /// <param name="ct">Cancels the capture. git and its children are stopped; the next capture clears what the interrupted one left.</param>
    /// <returns>Whether a point was recorded, and the branch tip afterwards.</returns>
    /// <exception cref="Savepoint.Core.Errors.PathSafetyException">The work tree is a protected location.</exception>
    /// <exception cref="Savepoint.Core.Errors.ConsentMarkerException">The folder has no marker for this game and role.</exception>
    /// <exception cref="Savepoint.Core.Errors.DiskSpaceException">The storage volume is below the free-space floor; nothing was written.</exception>
    /// <exception cref="Savepoint.Core.Errors.DetachedCaptureNotSupportedException">The folder is on an older point and the mode does not allow capturing there.</exception>
    /// <exception cref="Savepoint.Core.Errors.AmendNotAvailableException">An amend was asked for where the branch is not checked out or has no point.</exception>
    /// <exception cref="Savepoint.Core.Errors.OperationCancelledByUserException">The token was cancelled.</exception>
    /// <exception cref="Savepoint.Core.Errors.NestedRepositoryException">A folder in the game keeps its own history; nothing was staged.</exception>
    /// <exception cref="Savepoint.Core.Errors.SessionMovedException">A fork or move forward was asked for, but the folder is not on the session's start point or after it.</exception>
    /// <exception cref="Savepoint.Core.Errors.ProfileAlreadyExistsException">A fork was asked for onto a branch that exists at a different point.</exception>
    /// <exception cref="Savepoint.Core.Errors.ProfileHasNoPointsException">A move forward was asked for on a branch that has no point.</exception>
    /// <exception cref="Savepoint.Core.Errors.GitOperationException">git failed, or the request does not fit the repository's state.</exception>
    public async Task<CaptureOutcome> CaptureAsync(RepoRef repo, CaptureRequest request, IProgress<FileProgress>? progress = null, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        repo = Normalize(repo);
        try
        {
            ct.ThrowIfCancellationRequested();
            return await CaptureCoreAsync(repo, request, progress, ct).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw new OperationCancelledByUserException(CancelledOperation.Capture, repo.GameId);
        }
    }

    /// <summary>
    /// Removes the lock files a killed git process leaves behind (<c>index.lock</c>, <c>HEAD.lock</c>, ref and config
    /// locks). Call it only when no SavePoint git process is running for the repository; the engine does this itself
    /// before every write when it has no operation of its own in flight on the repository.
    /// </summary>
    /// <param name="gitDir">The git directory.</param>
    /// <returns>How many lock files were removed; 0 when the folder is not a git directory or this engine is working on it.</returns>
    public int RemoveStaleLocks(string gitDir)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gitDir);
        gitDir = Path.TrimEndingDirectorySeparator(Path.GetFullPath(gitDir));
        if (!IsInitialized(gitDir) || _inFlight.IsBusy(gitDir))
        {
            return 0;
        }

        return StaleLocks.RemoveAll(gitDir);
    }

    private async Task<CaptureOutcome> CaptureCoreAsync(RepoRef repo, CaptureRequest request, IProgress<FileProgress>? progress, CancellationToken ct)
    {
        CheckWorkTree(repo);
        RepoMarker.Verify(repo);

        // A folder a stopped switch left half done holds files of two points; recording it would save a mixture that never existed.
        if (SwitchJournal.Read(repo.GitDir) is not null)
        {
            throw new DirtyWorkingTreeException(repo.Role);
        }

        EnsureStorageSpace(repo);

        using var scope = BeginWrite(repo, request.IgnorePatterns);
        ct.ThrowIfCancellationRequested();

        var head = GitDirFiles.ReadHead(repo.GitDir);
        var tip = head.IsDetached ? head.DetachedCommit : await ResolveBranchAsync(repo, head.Branch!, ct).ConfigureAwait(false);

        return request.Mode switch
        {
            CaptureMode.Normal => await CaptureNormalAsync(repo, request, head, tip, progress, ct).ConfigureAwait(false),
            CaptureMode.Amend => await CaptureAmendAsync(repo, request, head, tip, progress, ct).ConfigureAwait(false),
            CaptureMode.Fork => await CaptureForkAsync(repo, request, head, tip, progress, ct).ConfigureAwait(false),
            CaptureMode.MoveForward => await CaptureMoveForwardAsync(repo, request, head, tip, progress, ct).ConfigureAwait(false),
            _ => throw new ArgumentOutOfRangeException(nameof(request), request.Mode, "Unknown capture mode."),
        };
    }

    private async Task<CaptureOutcome> CaptureAmendAsync(
        RepoRef repo,
        CaptureRequest request,
        HeadState head,
        string? tip,
        IProgress<FileProgress>? progress,
        CancellationToken ct)
    {
        if (head.IsDetached || tip is null || !string.Equals(head.Branch, request.Branch, StringComparison.Ordinal))
        {
            throw new AmendNotAvailableException();
        }

        await StageAsync(repo, progress, ct).ConfigureAwait(false);
        await CommitAsync(repo, request, amend: true, ct).ConfigureAwait(false);
        return new CaptureOutcome(true, await ResolveBranchAsync(repo, request.Branch, ct).ConfigureAwait(false));
    }

    private async Task<CaptureOutcome> CaptureForkAsync(
        RepoRef repo,
        CaptureRequest request,
        HeadState head,
        string? tip,
        IProgress<FileProgress>? progress,
        CancellationToken ct)
    {
        // Nothing to branch from yet: the new profile simply starts the repository.
        if (tip is null)
        {
            return await CaptureNormalAsync(repo, request, head, tip, progress, ct).ConfigureAwait(false);
        }

        await RequireSessionStartAsync(repo, request.ExpectedStartCommit, tip, ct).ConfigureAwait(false);

        // A branch left at this very point by an earlier attempt that was stopped is picked up, not refused.
        var existing = await ResolveBranchAsync(repo, request.Branch, ct).ConfigureAwait(false);
        if (existing is not null && !string.Equals(existing, tip, StringComparison.Ordinal))
        {
            throw new ProfileAlreadyExistsException(request.Branch);
        }

        await StageAsync(repo, progress, ct).ConfigureAwait(false);
        var changed = await IndexDiffersAsync(repo, "HEAD", ct).ConfigureAwait(false);

        if (existing is null)
        {
            await CreateBranchRefAsync(repo, request.Branch, tip, "savepoint: fork branch", ct).ConfigureAwait(false);
        }

        return await CommitOnBranchAsync(repo, request, head, changed, ct).ConfigureAwait(false);
    }

    private async Task<CaptureOutcome> CaptureMoveForwardAsync(
        RepoRef repo,
        CaptureRequest request,
        HeadState head,
        string? tip,
        IProgress<FileProgress>? progress,
        CancellationToken ct)
    {
        if (request.ExpectedStartCommit is null)
        {
            throw new ArgumentException("Moving a branch forward needs the commit the session started from.", nameof(request));
        }

        var branchTip = await ResolveBranchAsync(repo, request.Branch, ct).ConfigureAwait(false)
            ?? throw new ProfileHasNoPointsException(request.Branch);
        if (tip is null)
        {
            throw new SessionMovedException(repo.Role, "The folder is not on any point, so a branch cannot be moved to it.");
        }

        await RequireSessionStartAsync(repo, request.ExpectedStartCommit, tip, ct).ConfigureAwait(false);

        // Moving "forward" only ever drops the points after where the folder is: that point has to be part of the branch's history.
        var moves = !string.Equals(tip, branchTip, StringComparison.Ordinal);
        if (moves && !await IsAncestorAsync(repo, tip, branchTip, ct).ConfigureAwait(false))
        {
            throw new SessionMovedException(repo.Role, $"The point the folder is on is not part of the history of '{request.Branch}'.");
        }

        await StageAsync(repo, progress, ct).ConfigureAwait(false);
        var changed = await IndexDiffersAsync(repo, "HEAD", ct).ConfigureAwait(false);

        if (moves)
        {
            // The old value is checked, so a branch that moved since it was read is not overwritten.
            await RunWriteAsync(
                () => Invocation(repo, readOnly: false, "update-ref", "-m", "savepoint: move branch", GitDirFiles.BranchPrefix + request.Branch, tip, branchTip),
                ct).ConfigureAwait(false);
        }

        return await CommitOnBranchAsync(repo, request, head, changed, ct).ConfigureAwait(false);
    }

    // Attaches HEAD to the branch (the branch already points at the folder's point, so no file changes) and commits when something changed.
    private async Task<CaptureOutcome> CommitOnBranchAsync(RepoRef repo, CaptureRequest request, HeadState head, bool changed, CancellationToken ct)
    {
        if (!string.Equals(head.Branch, request.Branch, StringComparison.Ordinal))
        {
            await SetHeadAsync(repo, request.Branch, ct).ConfigureAwait(false);
        }

        if (changed)
        {
            await CommitAsync(repo, request, amend: false, ct).ConfigureAwait(false);
        }

        return new CaptureOutcome(changed, await ResolveBranchAsync(repo, request.Branch, ct).ConfigureAwait(false));
    }

    // The folder has to be on the point the session started from, or on a later one; anything else means it was changed behind the session's back.
    private async Task RequireSessionStartAsync(RepoRef repo, string? expectedStart, string current, CancellationToken ct)
    {
        if (expectedStart is null)
        {
            return;
        }

        if (!GitDirFiles.IsObjectId(expectedStart))
        {
            throw new ArgumentException("The session start must be a full commit id.", nameof(expectedStart));
        }

        if (!await IsAncestorAsync(repo, expectedStart, current, ct).ConfigureAwait(false))
        {
            throw new SessionMovedException(repo.Role, $"The folder is not on the point the session started from ({expectedStart}) or after it.");
        }
    }

    // True when <ancestor> is <descendant> or lies behind it. A point git does not know is an error, not "no".
    private async Task<bool> IsAncestorAsync(RepoRef repo, string ancestor, string descendant, CancellationToken ct)
    {
        var result = await RunAsync(GitDirOnly(repo, "merge-base", "--is-ancestor", ancestor, descendant) with { AllowedExitCodes = [0, 1] }, ct).ConfigureAwait(false);
        return result.ExitCode == 0;
    }

    private async Task<CaptureOutcome> CaptureNormalAsync(
        RepoRef repo,
        CaptureRequest request,
        HeadState head,
        string? tip,
        IProgress<FileProgress>? progress,
        CancellationToken ct)
    {
        if (head.IsDetached)
        {
            throw new DetachedCaptureNotSupportedException(repo.Role);
        }

        // A repository with no commit yet starts whichever branch is asked for; otherwise the checked-out branch is the only one that can be captured.
        if (tip is not null && !string.Equals(head.Branch, request.Branch, StringComparison.Ordinal))
        {
            throw new GitOperationException(null, $"Cannot capture onto '{request.Branch}' while '{head.Branch}' is checked out; switch to it first.");
        }

        await StageAsync(repo, progress, ct).ConfigureAwait(false);
        if (!await IndexDiffersAsync(repo, tip is null ? GitDirFiles.EmptyTree : "HEAD", ct).ConfigureAwait(false))
        {
            return new CaptureOutcome(false, tip);
        }

        if (tip is null)
        {
            await SetHeadAsync(repo, request.Branch, ct).ConfigureAwait(false);
        }

        await CommitAsync(repo, request, amend: false, ct).ConfigureAwait(false);
        return new CaptureOutcome(true, await ResolveBranchAsync(repo, request.Branch, ct).ConfigureAwait(false));
    }

    // Puts the folder's current state in the index. The files are listed here and handed to git by name, so only SavePoint's
    // exclude list decides what is left out. git prints one line per file it adds or removes, which feeds the progress reports.
    private async Task StageAsync(RepoRef repo, IProgress<FileProgress>? progress, CancellationToken ct)
    {
        var listing = await ListForStagingAsync(repo, ct).ConfigureAwait(false);
        LinkedFolders.RefuseAny(repo.WorkTree, listing.Files, ct);
        NestedRepositories.RefuseAny(repo.WorkTree, listing, ct);

        // An empty list would not mean "nothing": add -A with no paths takes the whole folder, excluded files included.
        if (listing.Files.Count == 0)
        {
            return;
        }

        var reporter = progress is null ? null : new ProgressReporter(progress);
        reporter?.SetTotal(listing.Files.Count);

        // The names go on stdin and are literal (a file called [a].txt is not a pattern); -f is safe because the list was made with the exclude list applied.
        var staging = Invocation(repo, readOnly: false, reporter is null ? ["add", "-A", "-f"] : ["add", "-A", "-f", "--verbose"]).WithPathspecs(listing.Files);
        await RunWriteAsync(
            () => staging with { OnStdoutLine = reporter is null ? null : line => ObserveAddLine(reporter, line) },
            ct).ConfigureAwait(false);
        reporter?.Complete();
    }

    private static void ObserveAddLine(ProgressReporter reporter, string line)
    {
        // The path sits between fixed text, so a quote inside a file name needs no unescaping.
        if (TryTakePath(line, "add '", out var added))
        {
            reporter.FileHandled(added);
        }
        else if (TryTakePath(line, "remove '", out var removed))
        {
            reporter.FileHandled(removed);
        }
    }

    private static bool TryTakePath(string line, string prefix, out string path)
    {
        if (line.StartsWith(prefix, StringComparison.Ordinal) && line.EndsWith('\'') && line.Length > prefix.Length)
        {
            path = line[prefix.Length..^1];
            return true;
        }

        path = string.Empty;
        return false;
    }

    // True when the index differs from the tree; used to tell "nothing changed" before a commit is attempted.
    private async Task<bool> IndexDiffersAsync(RepoRef repo, string treeish, CancellationToken ct)
    {
        var result = await RunAsync(
            Invocation(repo, readOnly: true, "diff-index", "--cached", "--quiet", treeish, "--") with { AllowedExitCodes = [0, 1] },
            ct).ConfigureAwait(false);
        return result.ExitCode == 1;
    }

    private Task SetHeadAsync(RepoRef repo, string branch, CancellationToken ct) =>
        RunWriteAsync(() => Invocation(repo, readOnly: false, "symbolic-ref", "HEAD", GitDirFiles.BranchPrefix + branch), ct);

    private Task CommitAsync(RepoRef repo, CaptureRequest request, bool amend, CancellationToken ct)
    {
        var message = CommitMessage.Compose(request.Message, request.CaptureId);
        string[] arguments = amend
            ? ["commit", "--quiet", "--amend", "--reset-author", "--allow-empty", "--no-verify", "--cleanup=verbatim", "-F", "-"]
            : ["commit", "--quiet", "--no-verify", "--cleanup=verbatim", "-F", "-"];
        return RunWriteAsync(() => Invocation(repo, readOnly: false, arguments) with { Stdin = message }, ct);
    }

    // The commit a branch points at: the loose ref file when there is one, git otherwise (the ref may be packed, or the branch may not exist).
    private async Task<string?> ResolveBranchAsync(RepoRef repo, string branch, CancellationToken ct)
    {
        if (GitDirFiles.ReadLooseRef(repo.GitDir, GitDirFiles.BranchPrefix + branch) is { } loose)
        {
            return loose;
        }

        var result = await RunAsync(
            GitDirOnly(repo, "rev-parse", "--verify", "-q", GitDirFiles.BranchPrefix + branch + "^{commit}") with { AllowedExitCodes = [0, 1] },
            ct).ConfigureAwait(false);
        return result.ExitCode == 0 ? result.StandardOutput.Trim() : null;
    }
}
