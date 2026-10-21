using Savepoint.Core.Api;
using Savepoint.Core.Backend;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;
using Savepoint.Core.Git;
using Savepoint.Core.Services.Jobs;
using Savepoint.Core.Services.Storage;

namespace Savepoint.Core.Services.Profiles;

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
    /// <param name="creating">True when the profile does not exist yet: its row is saved with the first folder that works, and removed branches left by a total failure.</param>
    /// <param name="onFinished">Told how the job ended, with the roles that failed.</param>
    public Guid Start(GameDefinition game, Profile profile, string pointName, IReadOnlyList<RepoRole> roles, string captureId, bool creating, Action<CaptureResult>? onFinished = null) =>
        _services.Jobs.Start(
            JobKind.Capture,
            game.Id,
            cancellable: true,
            async context =>
            {
                var result = await _services.RunForGameAsync(
                    game.Id,
                    (storage, _) => RunAsync(context, storage, game, profile, pointName, roles, captureId, creating),
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
        bool creating)
    {
        var repos = GameFolders.Repos(_services, storage, game).Where(repo => roles.Contains(repo.Role)).ToList();
        var outcomes = new List<FolderOutcome>();
        var started = new List<RepoRef>();
        var recorded = false;
        var progress = new Reporter(context);

        foreach (var repo in repos)
        {
            if (context.CancellationToken.IsCancellationRequested)
            {
                outcomes.Add(new FolderOutcome(repo.Role, ErrorInfo.From(new OperationCancelledByUserException(CancelledOperation.Capture, game.Id))));
                continue;
            }

            try
            {
                var existed = await storage.Engine.BranchExistsAsync(repo, profile.BranchName, context.CancellationToken).ConfigureAwait(false);
                if (!existed)
                {
                    started.Add(repo);
                }

                recorded |= await CaptureFolderAsync(storage, game, repo, profile.BranchName, existed, pointName, captureId, progress, context.CancellationToken).ConfigureAwait(false);
                outcomes.Add(new FolderOutcome(repo.Role));
            }
            catch (Exception e) when (e is not (OutOfMemoryException or StackOverflowException))
            {
                // A stop the user asked for reads as a cancelled save, not as an unknown failure.
                var error = e is OperationCanceledException ? new OperationCancelledByUserException(CancelledOperation.Capture, game.Id) : e;
                outcomes.Add(new FolderOutcome(repo.Role, ErrorInfo.From(error)));
            }
        }

        if (creating)
        {
            await FinishCreationAsync(storage, profile, started, outcomes).ConfigureAwait(false);
        }

        return new CaptureResult(profile.Id, recorded ? new HistoryPointId(captureId) : null, outcomes);
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
        IProgress<FileProgress> progress,
        CancellationToken cancellationToken)
    {
        // A branch that does not exist yet is started at what the folder holds now, whatever branch it is on.
        var request = new CaptureRequest(branch, pointName, captureId)
        {
            Mode = branchExists ? CaptureMode.Normal : CaptureMode.Fork,
            IgnorePatterns = game.IgnorePaths,
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
    private async Task FinishCreationAsync(StorageSnapshot storage, Profile profile, List<RepoRef> started, List<FolderOutcome> outcomes)
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

    // Progress reaches the job as it happens, so the bar never lags behind the files.
    private sealed class Reporter(JobContext context) : IProgress<FileProgress>
    {
        public void Report(FileProgress value) =>
            context.Progress.Report(new JobProgress(value.FilesDone, value.FilesTotal, null, null, value.CurrentPath));
    }
}
