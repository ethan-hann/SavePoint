using System.Text;
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
        if (!Directory.Exists(gitDir) || _inFlight.IsBusy(gitDir) || !GitDirRemoval.LooksLikeGitDir(gitDir))
        {
            return 0;
        }

        return StaleLocks.RemoveAll(gitDir);
    }

    private async Task<CaptureOutcome> CaptureCoreAsync(RepoRef repo, CaptureRequest request, IProgress<FileProgress>? progress, CancellationToken ct)
    {
        CheckWorkTree(repo);
        RepoMarker.Verify(repo);
        EnsureStorageSpace(repo);

        using var scope = _inFlight.Enter(repo.GitDir);
        PrepareForWrite(repo, scope, request.IgnorePatterns);
        ct.ThrowIfCancellationRequested();

        var head = GitDirFiles.ReadHead(repo.GitDir);
        var tip = head.IsDetached ? head.DetachedCommit : await ResolveBranchAsync(repo, head.Branch!, ct).ConfigureAwait(false);

        return request.Mode switch
        {
            CaptureMode.Normal => await CaptureNormalAsync(repo, request, head, tip, progress, ct).ConfigureAwait(false),
            _ => throw new NotSupportedException($"Capture mode {request.Mode} is not available yet."),
        };
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

        await StageAsync(repo, hasCommits: tip is not null, progress, ct).ConfigureAwait(false);
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

    // Puts the folder's current state in the index. git prints one line per file it adds or removes, which feeds the progress reports.
    private async Task StageAsync(RepoRef repo, bool hasCommits, IProgress<FileProgress>? progress, CancellationToken ct)
    {
        if (progress is null)
        {
            await RunWriteAsync(() => Invocation(repo, readOnly: false, "add", "-A"), ct).ConfigureAwait(false);
            return;
        }

        var reporter = new ProgressReporter(progress);
        reporter.SetTotal(await CountCandidatesAsync(repo, hasCommits, ct).ConfigureAwait(false));
        await RunWriteAsync(
            () => Invocation(repo, readOnly: false, "add", "-A", "--verbose") with { OnStdoutLine = line => ObserveAddLine(reporter, line) },
            ct).ConfigureAwait(false);
        reporter.Complete();
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

    // The files a capture is about to record: what differs from the last point, plus what is new and not ignored.
    private async Task<int?> CountCandidatesAsync(RepoRef repo, bool hasCommits, CancellationToken ct)
    {
        try
        {
            var untracked = RunAsync(Invocation(repo, readOnly: true, "ls-files", "-z", "--others", "--exclude-standard"), ct);
            var changed = hasCommits
                ? RunAsync(Invocation(repo, readOnly: true, "diff", "--name-only", "-z", "--no-renames", "HEAD", "--"), ct)
                : null;

            var names = new HashSet<string>(StringComparer.Ordinal);
            names.UnionWith((await untracked.ConfigureAwait(false)).SplitStandardOutputOnNul());
            if (changed is not null)
            {
                names.UnionWith((await changed.ConfigureAwait(false)).SplitStandardOutputOnNul());
            }

            return names.Count;
        }
        catch (GitOperationException)
        {
            // The count only decorates the progress bar.
            return null;
        }
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
        var message = new StringBuilder(request.Message).Append("\n\nSavepoint-Capture: ").Append(request.CaptureId).Append('\n').ToString();
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
            Invocation(repo, readOnly: true, "rev-parse", "--verify", "-q", GitDirFiles.BranchPrefix + branch + "^{commit}") with { AllowedExitCodes = [0, 1] },
            ct).ConfigureAwait(false);
        return result.ExitCode == 0 ? result.StandardOutput.Trim() : null;
    }
}
