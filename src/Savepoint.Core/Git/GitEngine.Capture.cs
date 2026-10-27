using Savepoint.Core.Errors;

namespace Savepoint.Core.Git;

public sealed partial class GitEngine
{
    /// <summary>
    /// Records the folder's current contents as a point on a branch, as described by <paramref name="request"/>.
    /// Nothing in the work tree is written or switched. A file named by the request's ignore patterns, or the consent marker, is
    /// not recorded: a new one is not added, and one an earlier point holds is left out of this point and every later one (the earlier
    /// points keep it, and the file on disk stays as it is). A save with nothing else to record still writes no point. A fork, a root or a move forward writes its new point first and then creates or
    /// moves the branch with one ref update, so a stop or a failure before that update leaves every branch and HEAD where they were.
    /// A move forward and an amend take commits off the branch: each one is first kept by a record outside every branch (see <see cref="ListRemovedAsync(RepoRef, IReadOnlyCollection{string}, CancellationToken)"/>),
    /// written in one ref transaction before the branch is touched, so a stop at any moment leaves a commit either still on the branch or recorded, never neither.
    /// </summary>
    /// <param name="repo">The folder to capture; its repository must exist.</param>
    /// <param name="request">The branch, message, capture id, mode and ignore patterns.</param>
    /// <param name="progress">Receives files and bytes done and expected while git works, starting with a report of the totals; may be null.</param>
    /// <param name="ct">Cancels the capture. git and its children are stopped; the next capture clears what the interrupted one left. Once a fork or a move forward has written its branch, HEAD is attached to it whatever the token says, and a cancel that arrives while git finishes that write does not turn the capture into a cancelled one.</param>
    /// <returns>Whether a point was recorded, and the branch tip afterwards.</returns>
    /// <exception cref="Savepoint.Core.Errors.PathSafetyException">The work tree is a protected location.</exception>
    /// <exception cref="Savepoint.Core.Errors.PathMissingException">The work tree does not exist.</exception>
    /// <exception cref="Savepoint.Core.Errors.ConsentMarkerException">The folder has no marker for this game and role.</exception>
    /// <exception cref="Savepoint.Core.Errors.InterruptedSwitchException">A stopped switch left the folder half done; finish it or discard first, so the mixture is never recorded.</exception>
    /// <exception cref="Savepoint.Core.Errors.LinkedFolderException">On Windows, a folder link holds files the capture would read; nothing was staged.</exception>
    /// <exception cref="Savepoint.Core.Errors.DiskSpaceException">The storage volume has less free than the floor plus the size of the files the point would add (the exception reports both figures); no file was stored. Temporary packs a stopped capture left are removed first when they are what fills the volume.</exception>
    /// <exception cref="Savepoint.Core.Errors.DetachedCaptureNotSupportedException">The folder is on an older point and the mode does not allow capturing there.</exception>
    /// <exception cref="Savepoint.Core.Errors.ProfileNotLoadedException">A plain capture was asked for onto a branch while the folder is on another one; nothing was staged.</exception>
    /// <exception cref="Savepoint.Core.Errors.AmendNotAvailableException">An amend was asked for where the branch is not checked out or has no point.</exception>
    /// <exception cref="Savepoint.Core.Errors.OperationCancelledByUserException">The token was cancelled.</exception>
    /// <exception cref="Savepoint.Core.Errors.NestedRepositoryException">A folder in the game keeps its own history; nothing was staged.</exception>
    /// <exception cref="Savepoint.Core.Errors.SessionMovedException">A fork or move forward was asked for, but the folder is not on the session's start point or after it.</exception>
    /// <exception cref="Savepoint.Core.Errors.ProfileAlreadyExistsException">A fork or a root was asked for onto a branch that exists at a different point.</exception>
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
    /// Removes what a killed git process leaves behind: its lock files (<c>index.lock</c>, <c>HEAD.lock</c>, ref and config
    /// locks) and the temporary pack and object files of a write it never finished (which can be hundreds of megabytes).
    /// Call it only when no SavePoint git process is running for the repository; the engine does this itself before every
    /// write when it has no operation of its own in flight on the repository.
    /// </summary>
    /// <param name="gitDir">The git directory.</param>
    /// <returns>How many files were removed; 0 when the folder is not a git directory, this engine is working on it, or a git an earlier SavePoint left running could not be ended.</returns>
    public int RemoveStaleLocks(string gitDir)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(gitDir);
        gitDir = Path.TrimEndingDirectorySeparator(Path.GetFullPath(gitDir));
        if (!IsInitialized(gitDir) || _inFlight.IsBusy(gitDir) || LeftoverGitRunning())
        {
            return 0;
        }

        return StaleLocks.RemoveAll(gitDir, _classifying.ContainsKey);
    }

    private async Task<CaptureOutcome> CaptureCoreAsync(RepoRef repo, CaptureRequest request, IProgress<FileProgress>? progress, CancellationToken ct)
    {
        CheckWorkTree(repo);
        RepoMarker.Verify(repo);
        await EnsureRepositoryAsync(repo, ct).ConfigureAwait(false);

        // A folder a stopped switch left half done holds files of two points; recording it would save a mixture that never existed.
        // A note that cannot be read or understood still says a switch started (it is read as one with no target).
        if (await ReadNoteAsync(repo, ct).ConfigureAwait(false) is { } unfinished)
        {
            throw new InterruptedSwitchException(repo.Role, unfinished.ToCommit);
        }

        EnsureStorageSpaceAfterLeftovers(repo);

        using var scope = BeginWrite(repo, request.IgnorePatterns);
        ct.ThrowIfCancellationRequested();

        // A mark a killed switch left behind would keep the file out of this point.
        await ClearSkipWorktreeAsync(repo, ct).ConfigureAwait(false);

        var head = GitDirFiles.ReadHead(repo.GitDir);
        var tip = head.IsDetached ? head.DetachedCommit : await ResolveBranchAsync(repo, head.Branch!, ct).ConfigureAwait(false);

        return request.Mode switch
        {
            CaptureMode.Normal => await CaptureNormalAsync(repo, request, head, tip, progress, ct).ConfigureAwait(false),
            CaptureMode.Amend => await CaptureAmendAsync(repo, request, head, tip, progress, ct).ConfigureAwait(false),
            CaptureMode.Fork => await CaptureForkAsync(repo, request, head, tip, progress, ct).ConfigureAwait(false),
            CaptureMode.Root => await CaptureRootAsync(repo, request, head, tip, progress, ct).ConfigureAwait(false),
            CaptureMode.MoveForward => await CaptureMoveForwardAsync(repo, request, head, tip, progress, ct).ConfigureAwait(false),
            _ => throw new ArgumentOutOfRangeException(nameof(request), request.Mode, "Unknown capture mode."),
        };
    }

    // The temporary packs a stopped write left can be most of what a full volume holds, and the write about to start would remove them anyway.
    // They go before the volume is judged, and only when nothing of this engine's, and no git an earlier run left going, is working on the repository.
    private void EnsureStorageSpaceAfterLeftovers(RepoRef repo)
    {
        try
        {
            EnsureStorageSpace(repo);
        }
        catch (DiskSpaceException)
        {
            if (!IsInitialized(repo.GitDir) || _inFlight.IsBusy(repo.GitDir) || LeftoverGitRunning() || StaleLocks.RemoveTemporaryFiles(repo.GitDir) == 0)
            {
                throw;
            }

            EnsureStorageSpace(repo);
        }
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
        await DropNamedFromIndexAsync(repo, ct).ConfigureAwait(false);
        if (request.SkipAmendWhenUnchanged && !await IndexDiffersAsync(repo, "HEAD", ct).ConfigureAwait(false))
        {
            return new CaptureOutcome(false, tip);
        }

        // The amend gives the branch a new commit and drops the one it had; that one is recorded first.
        await RecordRemovedAsync(repo, request.Branch, [tip], RemovalReason.Replaced, request.CapturedAt ?? _options.Clock.GetUtcNow(), ct).ConfigureAwait(false);
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

        // A branch left by an earlier attempt that was stopped is picked up, not refused: still at this point, or holding the point this capture recorded.
        var existing = await ResolveBranchAsync(repo, request.Branch, ct).ConfigureAwait(false);
        if (existing is not null && !string.Equals(existing, tip, StringComparison.Ordinal))
        {
            if (!await IsPointOfCaptureAsync(repo, existing, tip, request.CaptureId, ct).ConfigureAwait(false))
            {
                throw new ProfileAlreadyExistsException(request.Branch);
            }

            await AttachHeadAsync(repo, request.Branch, head).ConfigureAwait(false);
            return new CaptureOutcome(true, existing);
        }

        await StageAsync(repo, progress, ct).ConfigureAwait(false);
        var changed = await IndexDiffersAsync(repo, "HEAD", ct).ConfigureAwait(false);

        // The point exists before any ref changes, so a stop up to the one ref write leaves every ref where it was.
        if (changed)
        {
            await DropNamedFromIndexAsync(repo, ct).ConfigureAwait(false);
        }

        var commit = changed ? await WriteCommitAsync(repo, request, tip, ct).ConfigureAwait(false) : tip;
        if (existing is null)
        {
            await RefWriteAsync(repo, request.Branch, commit, () => CreateBranchRefAsync(repo, request.Branch, commit, "savepoint: fork branch", ct)).ConfigureAwait(false);
        }
        else if (changed)
        {
            await RefWriteAsync(repo, request.Branch, commit, () => MoveBranchRefAsync(repo, request.Branch, commit, existing, "savepoint: fork branch", ct)).ConfigureAwait(false);
        }

        await AttachHeadAsync(repo, request.Branch, head).ConfigureAwait(false);
        return new CaptureOutcome(changed, commit);
    }

    private async Task<CaptureOutcome> CaptureRootAsync(
        RepoRef repo,
        CaptureRequest request,
        HeadState head,
        string? tip,
        IProgress<FileProgress>? progress,
        CancellationToken ct)
    {
        // Nothing is saved here yet: the first capture of the repository already is a history of its own.
        if (tip is null)
        {
            return await CaptureNormalAsync(repo, request, head, tip, progress, ct).ConfigureAwait(false);
        }

        // A branch left by an earlier attempt that was stopped is picked up, not refused: it holds the point this capture recorded.
        var existing = await ResolveBranchAsync(repo, request.Branch, ct).ConfigureAwait(false);
        if (existing is not null)
        {
            if (!await IsPointOfCaptureAsync(repo, existing, null, request.CaptureId, ct).ConfigureAwait(false))
            {
                throw new ProfileAlreadyExistsException(request.Branch);
            }

            await AttachHeadAsync(repo, request.Branch, head).ConfigureAwait(false);
            return new CaptureOutcome(true, existing);
        }

        // Written before any ref changes, like a fork, so a stop up to the one ref write leaves every ref and HEAD where they were.
        await StageAsync(repo, progress, ct).ConfigureAwait(false);
        await DropNamedFromIndexAsync(repo, ct).ConfigureAwait(false);
        var commit = await WriteCommitAsync(repo, request, null, ct).ConfigureAwait(false);
        await RefWriteAsync(repo, request.Branch, commit, () => CreateBranchRefAsync(repo, request.Branch, commit, "savepoint: start branch", ct)).ConfigureAwait(false);
        await AttachHeadAsync(repo, request.Branch, head).ConfigureAwait(false);
        return new CaptureOutcome(true, commit);
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

        // An earlier attempt that moved the branch and was stopped before HEAD followed is picked up, not written again: the branch already holds
        // the point this capture recorded on top of the folder's commit, and a second one would list the first as a removed snapshot.
        if (moves && await IsPointOfCaptureAsync(repo, branchTip, tip, request.CaptureId, ct).ConfigureAwait(false))
        {
            await AttachHeadAsync(repo, request.Branch, head).ConfigureAwait(false);
            return new CaptureOutcome(true, branchTip);
        }

        await StageAsync(repo, progress, ct).ConfigureAwait(false);
        var changed = await IndexDiffersAsync(repo, "HEAD", ct).ConfigureAwait(false);

        // The point exists before the branch moves, so the points a move drops stay on it until the one ref write.
        if (changed)
        {
            await DropNamedFromIndexAsync(repo, ct).ConfigureAwait(false);
        }

        var commit = changed ? await WriteCommitAsync(repo, request, tip, ct).ConfigureAwait(false) : tip;
        if (!string.Equals(commit, branchTip, StringComparison.Ordinal))
        {
            // The dropped commits are recorded before the branch lets go of them, so no stop can leave one off the branch and unrecorded.
            if (moves)
            {
                var dropped = await CommitsBetweenAsync(repo, branchTip, tip, ct).ConfigureAwait(false);
                await RecordRemovedAsync(repo, request.Branch, dropped, RemovalReason.MovedBack, request.CapturedAt ?? _options.Clock.GetUtcNow(), ct).ConfigureAwait(false);
            }

            await RefWriteAsync(repo, request.Branch, commit, () => MoveBranchRefAsync(repo, request.Branch, commit, branchTip, "savepoint: move branch", ct)).ConfigureAwait(false);
        }

        await AttachHeadAsync(repo, request.Branch, head).ConfigureAwait(false);
        return new CaptureOutcome(changed, commit);
    }

    // A stop that reaches the ref write after git has already moved the branch has nothing left to stop: the point is recorded, so HEAD still follows it.
    private async Task RefWriteAsync(RepoRef repo, string branch, string commit, Func<Task> write)
    {
        try
        {
            await write().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            if (!string.Equals(await ResolveBranchAsync(repo, branch, CancellationToken.None).ConfigureAwait(false), commit, StringComparison.Ordinal))
            {
                throw;
            }
        }
    }

    // Writes the staged tree as a point on top of <parent> (a point with nothing behind it when null) and moves no ref; the id is all there is to it until a ref names it.
    private async Task<string> WriteCommitAsync(RepoRef repo, CaptureRequest request, string? parent, CancellationToken ct)
    {
        var tree = (await RunWriteAsync(() => Invocation(repo, readOnly: false, "write-tree"), ct).ConfigureAwait(false)).StandardOutput.Trim();
        var message = CommitMessage.Compose(request.Message, request.CaptureId, request.CapturedAt ?? _options.Clock.GetUtcNow(), await ListEmptyFoldersAsync(repo, ct).ConfigureAwait(false));
        string[] arguments = parent is null ? ["commit-tree", tree, "-F", "-"] : ["commit-tree", tree, "-p", parent, "-F", "-"];
        var result = await RunWriteAsync(() => Invocation(repo, readOnly: false, arguments) with { Stdin = message }, ct).ConfigureAwait(false);
        return result.StandardOutput.Trim();
    }

    // The branch the point went on takes HEAD with it (no file changes: the folder already holds that point's files).
    // Not cancellable: the branch has moved by now, and a cancel here would leave HEAD somewhere the branch is not.
    private Task AttachHeadAsync(RepoRef repo, string branch, HeadState head) =>
        string.Equals(head.Branch, branch, StringComparison.Ordinal) ? Task.CompletedTask : SetHeadAsync(repo, branch, CancellationToken.None);

    // True when <commit> is the point a capture with this id recorded straight on top of <parent> (with nothing behind it when null): what a stopped attempt of the same capture left.
    private async Task<bool> IsPointOfCaptureAsync(RepoRef repo, string commit, string? parent, string captureId, CancellationToken ct)
    {
        var result = await RunAsync(
            GitDirOnly(repo, "log", "-1", "--format=%P%x1f%(trailers:key=Savepoint-Capture,valueonly,separator=%x2c)", commit, "--"),
            ct).ConfigureAwait(false);

        // The unit separator counts as white space, so only the line end is trimmed or a point with no parent would lose its first field.
        var fields = result.StandardOutput.TrimEnd('\r', '\n').Split('\x1f');
        return fields.Length == 2 && string.Equals(fields[0].Trim(), parent ?? string.Empty, StringComparison.Ordinal) && string.Equals(fields[1].Trim(), captureId, StringComparison.Ordinal);
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
            throw new ProfileNotLoadedException(request.Branch, repo.Role);
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

        await DropNamedFromIndexAsync(repo, ct).ConfigureAwait(false);
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

        var reporter = progress is null ? null : new ProgressReporter(progress, time: _options.Clock);

        // A stopped or failed attempt may have staged some of the files already; they are not listed again, but they are part of this save.
        var stagedBefore = reporter is null ? 0 : await CountStagedBeforeAsync(repo, listing, ct).ConfigureAwait(false);

        // An empty list would not mean "nothing": add -A with no paths takes the whole folder, excluded files included.
        if (listing.Files.Count == 0)
        {
            if (reporter is not null && stagedBefore > 0)
            {
                reporter.SetTotal(stagedBefore);
                reporter.SetAlreadyDone(stagedBefore);
                reporter.SetByteTotal(0);
                reporter.Start();
                reporter.Complete();
            }

            return;
        }

        var total = listing.Files.Count + stagedBefore;
        reporter?.SetTotal(total);
        reporter?.SetAlreadyDone(stagedBefore);

        // Asked afresh each time: adding a file that replaces a folder drops the folder's recorded files from the index.
        async Task<HashSet<string>> TrackedAsync() =>
            [.. (await RunAsync(Invocation(repo, readOnly: true, "ls-files", "-z", "--cached"), ct).ConfigureAwait(false)).SplitStandardOutputOnNul()];

        // A recorded file that is gone (or is now a folder) is dropped from the index by name. Handing it to add as a path would match
        // everything under it, and -f would store the excluded files there too.
        var (removals, additions) = SplitRemovals(repo, listing.Files, await TrackedAsync().ConfigureAwait(false));
        IReadOnlyList<StagingBatch> batches = additions.Count == 0 ? [] : [new StagingBatch(additions, [], false)];

        // Packs are written without compression, so the store grows by about the files' own size; refuse before git writes any of it.
        // The floor stays on top, for the repack that follows.
        var sizes = additions.Count == 0 ? [] : await SizesOfAsync(repo, additions, ct).ConfigureAwait(false);
        EnsureStorageSpace(repo, sizes.Sum());
        if (reporter is not null)
        {
            reporter.SetByteTotal(sizes.Sum());
            reporter.Start();

            // git holds a file's name back until its whole add ends, so a watched capture adds in groups to hear the names as it goes.
            batches = StagingBatches.Plan(additions, sizes, _options.StagingLimits);
        }

        // git prints a file's line only once it is stored, and holds the lines back while it works, so a large file shows nothing
        // there for minutes; the store's growth is what moves the bar meanwhile.
        using var stopWatching = new CancellationTokenSource();
        var meter = reporter is null ? null : StoreMeterOf(repo);
        var watching = reporter is null ? Task.CompletedTask : WatchStoreAsync(meter!, reporter, stopWatching.Token);

        var staged = false;
        try
        {
            if (removals.Count > 0)
            {
                await RunWriteAsync(() => Invocation(repo, readOnly: false, "update-index", "--force-remove", "-z", "--stdin") with { Stdin = NulList(removals) }, ct).ConfigureAwait(false);
                foreach (var removed in removals)
                {
                    reporter?.FileHandled(removed);
                }
            }

            foreach (var planned in batches)
            {
                var batch = planned;
                if (reporter is not null)
                {
                    // A file can vanish between the listing and its add, and git stops at a name it cannot find; one that is recorded is
                    // still passed, because its removal is what the add records.
                    if (await WithoutVanishedAsync(repo, batch, TrackedAsync).ConfigureAwait(false) is not { } present)
                    {
                        total -= batch.Files.Count;
                        reporter.SetTotal(total);
                        continue;
                    }

                    total -= batch.Files.Count - present.Files.Count;
                    reporter.SetTotal(total);
                    batch = present.Big ? StagingBatches.AsGitTakesThem(present, (await TrackedAsync().ConfigureAwait(false)).Contains) : present;
                }

                // The names go on stdin and are literal (a file called [a].txt is not a pattern); -f is safe because the list was made with the exclude list applied.
                var staging = Invocation(repo, readOnly: false, reporter is null ? ["add", "-A", "-f"] : ["add", "-A", "-f", "--verbose"]).WithPathspecs(batch.Files);
                if (BigFileEnvironment() is { } environment)
                {
                    staging = staging with { ExtraEnvironment = new Dictionary<string, string?>(staging.ExtraEnvironment!.Concat(environment)) };
                }

                reporter?.BeginBatch(batch.Big ? batch.Files : null, batch.Big ? batch.Sizes : null, meter?.Growth());
                var packsBefore = PackFilesOf(repo);
                try
                {
                    await RunWriteAsync(
                        () => staging with { OnStdoutLine = reporter is null ? null : line => ObserveAddLine(reporter, line) },
                        ct).ConfigureAwait(false);
                }
                finally
                {
                    // A pack an add leaves behind holds the files it streamed; even a stopped add may have finished one.
                    KeepPacksSince(repo, packsBefore);
                }

                reporter?.EndBatch();
            }

            staged = true;
        }
        catch (GitOperationException e) when (FirstHeldFileNamedBy(repo, e.StandardError ?? e.Message) is { } held)
        {
            // The file git says it could not index is the answer the player needs, when it is one nobody can read now.
            throw held.ToException(HeldFileOperation.Save);
        }
        finally
        {
            await stopWatching.CancelAsync().ConfigureAwait(false);
            await watching.ConfigureAwait(false);
            if (!staged)
            {
                reporter?.Abandon();
            }
        }

        reporter?.Complete();
    }

    // The plan sorts files by the size git streams into a pack; the repository file says that size is the default. A different size (only a
    // test makes one) is told to git the same way the engine's other forced settings are, so the two agree.
    private IReadOnlyDictionary<string, string?>? BigFileEnvironment()
    {
        var bytes = _options.StagingLimits.BigBytes;
        if (bytes == StagingLimits.Default.BigBytes)
        {
            return null;
        }

        var slot = GitBinary.ForcedConfigCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
        return new Dictionary<string, string?>
        {
            ["GIT_CONFIG_COUNT"] = (GitBinary.ForcedConfigCount + 1).ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["GIT_CONFIG_KEY_" + slot] = "core.bigFileThreshold",
            ["GIT_CONFIG_VALUE_" + slot] = bytes.ToString(System.Globalization.CultureInfo.InvariantCulture),
        };
    }

    // Often enough to move the bar for a large file, rarely enough that counting a big store costs little.
    private static readonly TimeSpan _sampleInterval = TimeSpan.FromMilliseconds(500);

    private StoreMeter StoreMeterOf(RepoRef repo)
    {
        var objects = Path.Combine(repo.GitDir, "objects");
        var size = _options.ObjectStoreSize ?? ObjectStoreMeter.Size;
        return new StoreMeter(() => size(objects));
    }

    private Task WatchStoreAsync(StoreMeter meter, ProgressReporter reporter, CancellationToken stop)
    {
        var wait = _options.SampleWait ?? (ct => Task.Delay(_sampleInterval, ct));
        return StagingByteSampler.RunAsync(meter, reporter, wait, stop);
    }

    // How many files an earlier attempt left staged that differ from the last point and are not in the listing (which holds only what
    // still differs from the index).
    private async Task<int> CountStagedBeforeAsync(RepoRef repo, WorkTreeListing listing, CancellationToken ct)
    {
        var head = await RunAsync(Invocation(repo, readOnly: true, "rev-parse", "--verify", "-q", "HEAD") with { AllowedExitCodes = [0, 1] }, ct).ConfigureAwait(false);
        var diff = await RunAsync(
            Invocation(repo, readOnly: true, "diff-index", "--cached", "--name-only", "-z", "--no-renames", head.ExitCode == 0 ? "HEAD" : GitDirFiles.EmptyTree, "--"),
            ct).ConfigureAwait(false);
        var staged = diff.SplitStandardOutputOnNul();
        if (staged.Count == 0)
        {
            return 0;
        }

        var listed = new HashSet<string>(listing.Files, StringComparer.Ordinal);
        var named = await RecordedNamedAsync(repo, ct).ConfigureAwait(false);
        return staged.Count(path => !listed.Contains(path) && !named.Contains(path));
    }

    // The listed files that are recorded and plainly gone from the folder (a file replaced by a folder counts as gone), apart from the rest.
    private (List<string> Removals, List<string> Additions) SplitRemovals(RepoRef repo, IReadOnlyList<string> files, HashSet<string> tracked)
    {
        var look = _options.PresenceProbe ?? (path => PathPresence.Look(path));
        var removals = new List<string>();
        var additions = new List<string>();
        foreach (var file in files)
        {
            (tracked.Contains(file) && look(FullPath(repo, file)) == Presence.Gone ? removals : additions).Add(file);
        }

        return (removals, additions);
    }

    // The batch without the files that are gone from the folder and not recorded (git has nothing to add or remove for them); null when
    // nothing is left.
    private async Task<StagingBatch?> WithoutVanishedAsync(RepoRef repo, StagingBatch batch, Func<Task<HashSet<string>>> tracked)
    {
        var look = _options.PresenceProbe ?? (path => PathPresence.Look(path));
        List<int>? gone = null;
        for (var i = 0; i < batch.Files.Count; i++)
        {
            if (look(FullPath(repo, batch.Files[i])) == Presence.Gone)
            {
                (gone ??= []).Add(i);
            }
        }

        if (gone is null)
        {
            return batch;
        }

        var known = await tracked().ConfigureAwait(false);
        var drop = gone.Where(i => !known.Contains(batch.Files[i])).ToHashSet();
        if (drop.Count == 0)
        {
            return batch;
        }

        var keep = Enumerable.Range(0, batch.Files.Count).Where(i => !drop.Contains(i)).ToList();
        return keep.Count == 0 ? null : new StagingBatch([.. keep.Select(i => batch.Files[i])], batch.Sizes.Count == 0 ? [] : [.. keep.Select(i => batch.Sizes[i])], batch.Big);
    }

    // The bytes each file will add to the store; a file that is gone counts for nothing.
    private static async Task<long[]> SizesOfAsync(RepoRef repo, IReadOnlyList<string> paths, CancellationToken ct)
    {
        var sizes = new long[paths.Count];
        await Task.Run(
            () => Parallel.For(0, paths.Count, new ParallelOptions { CancellationToken = ct, MaxDegreeOfParallelism = Environment.ProcessorCount }, i => sizes[i] = Math.Max(SizeOrMissing(repo, paths[i]), 0)),
            ct).ConfigureAwait(false);
        return sizes;
    }

    // Only the files git itself names are looked at: any other failure of adding the files is not a held file, whatever else is held.
    private static HeldFile? FirstHeldFileNamedBy(RepoRef repo, string gitMessage)
    {
        foreach (var path in GitFailure.UnindexedPaths(gitMessage))
        {
            var full = FullPath(repo, path);
            if (FileLockProbe.Probe(full) is { } kind)
            {
                return new HeldFile(full, kind);
            }
        }

        return null;
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

    private async Task CommitAsync(RepoRef repo, CaptureRequest request, bool amend, CancellationToken ct)
    {
        var message = CommitMessage.Compose(request.Message, request.CaptureId, request.CapturedAt ?? _options.Clock.GetUtcNow(), await ListEmptyFoldersAsync(repo, ct).ConfigureAwait(false));
        string[] arguments = amend
            ? ["commit", "--quiet", "--amend", "--reset-author", "--allow-empty", "--no-verify", "--cleanup=verbatim", "-F", "-"]
            : ["commit", "--quiet", "--no-verify", "--cleanup=verbatim", "-F", "-"];
        await RunWriteAsync(() => Invocation(repo, readOnly: false, arguments) with { Stdin = message }, ct).ConfigureAwait(false);
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
