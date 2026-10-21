using Savepoint.Core.Api;
using Savepoint.Core.Backend;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;
using Savepoint.Core.Git;
using Savepoint.Core.Services.Jobs;
using Savepoint.Core.Services.Storage;

namespace Savepoint.Core.Services.Profiles;

/// <summary>How one folder is saved when that differs from a plain capture: the mode, the commit a session began from, and whether a clean folder is left alone.</summary>
/// <param name="Mode">How the capture relates to the folder's history.</param>
/// <param name="ExpectedStartCommit">The commit the play session started from, which a fork or a move forward needs; null for none.</param>
/// <param name="SkipWhenClean">True to leave a folder with nothing unsaved untouched, so rewriting its latest point never turns it into an empty one.</param>
internal sealed record FolderCapturePlan(CaptureMode Mode, string? ExpectedStartCommit = null, bool SkipWhenClean = false);

/// <summary>Saves a profile's folders as one capture event: every folder it touches gets a point carrying the same capture id (D15).</summary>
internal sealed class CaptureRunner
{
    private readonly BackendServices _services;
    private readonly OptimizationService _optimization;

    public CaptureRunner(BackendServices services, OptimizationService optimization)
    {
        _services = services;
        _optimization = optimization;
    }

    /// <summary>
    /// Starts the capture job for a profile.
    /// </summary>
    /// <param name="game">The game being saved.</param>
    /// <param name="profile">The profile; its branch is the one that receives the points.</param>
    /// <param name="pointName">What the new point shows in history.</param>
    /// <param name="roles">The folders to save.</param>
    /// <param name="captureId">The id every folder's point carries.</param>
    /// <param name="creating">True when the profile does not exist yet: its row is saved with the first folder that works, and branches left by a total failure are taken back.</param>
    /// <param name="onFinished">Told how the job ended, with the outcome of each folder.</param>
    /// <param name="plans">How a folder is saved when it is not a plain capture, as the play session after an older point asks for; a folder it names no plan for is saved plainly.</param>
    public Guid Start(
        GameDefinition game,
        Profile profile,
        string pointName,
        IReadOnlyList<RepoRole> roles,
        string captureId,
        bool creating,
        Action<CaptureResult>? onFinished = null,
        IReadOnlyDictionary<RepoRole, FolderCapturePlan>? plans = null) =>
        _services.Jobs.Start(
            JobKind.Capture,
            game.Id,
            cancellable: true,
            async context =>
            {
                var result = await _services.RunForGameAsync(
                    game.Id,
                    (storage, _) => RunAsync(context, storage, game, profile, pointName, roles, captureId, creating, plans),
                    context.CancellationToken).ConfigureAwait(false);
                onFinished?.Invoke(result);

                // The tidy-up is queued after the lock is gone, and only for a save nobody stopped (D13.17).
                if (!context.CancellationToken.IsCancellationRequested && result.Folders.Any(folder => folder.Ok))
                {
                    await _optimization.QueueRoutineIfNeededAsync(game.Id).ConfigureAwait(false);
                }

                return result;
            });

    private async Task<CaptureResult> RunAsync(
        JobContext context,
        StorageSnapshot storage,
        GameDefinition game,
        Profile profile,
        string pointName,
        IReadOnlyList<RepoRole> roles,
        string captureId,
        bool creating,
        IReadOnlyDictionary<RepoRole, FolderCapturePlan>? plans)
    {
        var repos = GameFolders.Repos(_services, storage, game).Where(repo => roles.Contains(repo.Role)).ToList();
        var progress = new CombinedProgress(context);

        // The two folders are separate repositories, so they are saved side by side; one failing never stops the other.
        var saved = await Task.WhenAll(repos.Select(repo => SaveFolderAsync(context, storage, game, repo, profile.BranchName, pointName, captureId, plans?.GetValueOrDefault(repo.Role), progress.For(repo.Role)))).ConfigureAwait(false);

        var outcomes = saved.Select(folder => folder.Outcome).ToList();
        if (creating)
        {
            await FinishCreationAsync(storage, profile, saved.Where(folder => folder.StartedBranch).Select(folder => folder.Repo), outcomes).ConfigureAwait(false);
        }

        var recorded = saved.Any(folder => folder.Recorded);
        return new CaptureResult(profile.Id, recorded ? new HistoryPointId(captureId) : null, outcomes);
    }

    private async Task<SavedFolder> SaveFolderAsync(
        JobContext context,
        StorageSnapshot storage,
        GameDefinition game,
        RepoRef repo,
        string branch,
        string pointName,
        string captureId,
        FolderCapturePlan? plan,
        IProgress<FileProgress> progress)
    {
        if (context.CancellationToken.IsCancellationRequested)
        {
            return new SavedFolder(repo, new FolderOutcome(repo.Role, ErrorInfo.From(new OperationCancelledByUserException(CancelledOperation.Capture, game.Id))), false, false);
        }

        var startedBranch = false;
        try
        {
            var existed = await storage.Engine.BranchExistsAsync(repo, branch, context.CancellationToken).ConfigureAwait(false);
            startedBranch = !existed;
            var recorded = await CaptureFolderAsync(storage, game, repo, branch, existed, pointName, captureId, plan, progress, context.CancellationToken).ConfigureAwait(false);
            return new SavedFolder(repo, new FolderOutcome(repo.Role), recorded, startedBranch);
        }
        catch (Exception e) when (e is not (OutOfMemoryException or StackOverflowException))
        {
            // A stop the user asked for reads as a cancelled save, not as an unknown failure.
            var error = e is OperationCanceledException ? new OperationCancelledByUserException(CancelledOperation.Capture, game.Id) : e;
            return new SavedFolder(repo, new FolderOutcome(repo.Role, ErrorInfo.From(error)), false, startedBranch);
        }
    }

    // True when a new point was recorded in this folder.
    private static async Task<bool> CaptureFolderAsync(
        StorageSnapshot storage,
        GameDefinition game,
        RepoRef repo,
        string branch,
        bool branchExists,
        string pointName,
        string captureId,
        FolderCapturePlan? plan,
        IProgress<FileProgress> progress,
        CancellationToken cancellationToken)
    {
        if (plan is { SkipWhenClean: true } && (await storage.Engine.GetStatusAsync(repo, cancellationToken).ConfigureAwait(false)).IsClean)
        {
            return false;
        }

        // A branch that does not exist yet is started at what the folder holds now, whatever branch it is on.
        var request = new CaptureRequest(branch, pointName, captureId)
        {
            Mode = plan?.Mode ?? (branchExists ? CaptureMode.Normal : CaptureMode.Fork),
            IgnorePatterns = game.IgnorePaths,
            ExpectedStartCommit = plan?.ExpectedStartCommit,
        };
        var outcome = await storage.Engine.CaptureAsync(repo, request, progress, cancellationToken).ConfigureAwait(false);
        if (outcome.Changed || outcome.TipCommitId is not null)
        {
            return outcome.Changed;
        }

        // An empty folder in an empty repository has nothing to record; the profile still needs a branch here.
        await storage.Engine.CreateEmptyBranchAsync(repo, branch, pointName, captureId, cancellationToken).ConfigureAwait(false);
        return true;
    }

    // The profile exists once any folder was saved; when none was, the branches this attempt started are taken back (D23).
    private async Task FinishCreationAsync(StorageSnapshot storage, Profile profile, IEnumerable<RepoRef> started, List<FolderOutcome> outcomes)
    {
        if (outcomes.Any(outcome => outcome.Ok))
        {
            await _services.Catalog.SaveProfileAsync(profile).ConfigureAwait(false);
            return;
        }

        foreach (var repo in started)
        {
            try
            {
                await storage.Engine.DeleteBranchAsync(repo, profile.BranchName).ConfigureAwait(false);
            }
            catch (Exception e) when (e is SavepointException or IOException)
            {
                // A branch the engine could not take back is only a branch; no profile row points at it.
            }
        }
    }

    private sealed record SavedFolder(RepoRef Repo, FolderOutcome Outcome, bool Recorded, bool StartedBranch);

    // Both folders report into one bar: the files done and expected add up, and the total stays unknown until every folder has counted.
    private sealed class CombinedProgress(JobContext context)
    {
        private readonly Lock _gate = new();
        private readonly Dictionary<RepoRole, (int Done, int? Total)> _parts = [];
        private int _expected;

        public IProgress<FileProgress> For(RepoRole role)
        {
            lock (_gate)
            {
                _expected++;
            }

            return new Part(this, role);
        }

        private void Update(RepoRole role, FileProgress value)
        {
            lock (_gate)
            {
                _parts[role] = (value.FilesDone, value.FilesTotal);
                var done = _parts.Values.Sum(part => part.Done);
                var counted = _parts.Count == _expected && _parts.Values.All(part => part.Total is not null);
                context.Progress.Report(new JobProgress(done, counted ? _parts.Values.Sum(part => part.Total!.Value) : null, null, null, value.CurrentPath));
            }
        }

        private sealed class Part(CombinedProgress owner, RepoRole role) : IProgress<FileProgress>
        {
            public void Report(FileProgress value) => owner.Update(role, value);
        }
    }
}
