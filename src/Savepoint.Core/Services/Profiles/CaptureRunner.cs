using System.Collections.Concurrent;
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
/// <param name="AmendPointId">
/// For <see cref="CaptureMode.Amend"/>: the capture id of the one point being replaced. Only a folder whose newest commit carries it is
/// rewritten; a folder whose newest commit belongs to another point gets a new commit under the same capture id, so no other point is lost.
/// </param>
/// <param name="LeftAtLaunch">
/// True for a folder the launch left as it was. It may be on another profile's branch, which a new or updated point cannot be written
/// onto, so such a folder is skipped instead of failing the save and reported as left alone; a folder the profile has no branch in is
/// saved as its first history.
/// </param>
/// <param name="Downgraded">True for a folder whose replacement was refused at save time because rewriting its snapshot could touch another profile's history; it is saved as a new snapshot and reported as kept as new.</param>
internal sealed record FolderCapturePlan(CaptureMode Mode, string? ExpectedStartCommit = null, bool SkipWhenClean = false, string? AmendPointId = null, bool LeftAtLaunch = false, bool Downgraded = false);

/// <summary>One capture event: the id every folder's point carries, and when it began, which every folder's point records the same way.</summary>
/// <param name="Id">The capture id.</param>
/// <param name="At">When the event began.</param>
internal readonly record struct CaptureEvent(string Id, DateTimeOffset At);

/// <summary>Saves a profile's folders as one capture event: every folder it touches gets a point carrying the same capture id (D15).</summary>
internal sealed class CaptureRunner
{
    private readonly BackendServices _services;
    private readonly OptimizationService _optimization;

    // When a save reached only some of its folders, a later save of just the missing ones joins the same capture event.
    private readonly ConcurrentDictionary<(string GameId, string ProfileId), PartialSave> _partial = new();

    // What each folder of a replacement was judged to do, by capture id, so a retry of a save that only half worked does as the first try did.
    private readonly ConcurrentDictionary<string, IReadOnlyDictionary<RepoRole, FolderCapturePlan>> _judged = new(StringComparer.Ordinal);

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
    /// <param name="capture">The capture event: the id every folder's point carries and when the event began.</param>
    /// <param name="creating">True when the profile does not exist yet: its row is saved with the first folder that works, and branches left by a total failure are taken back.</param>
    /// <param name="plans">How a folder is saved when it is not a plain capture, as the play session after an older point asks for; a folder it names no plan for is saved plainly.</param>
    /// <param name="forksNewBranches">True for the retry of a creation that saved only some folders: the profile exists by now, but a folder it never saved still starts its branch from what the folder held, as the first try did.</param>
    public Guid Start(
        GameDefinition game,
        Profile profile,
        string pointName,
        IReadOnlyList<RepoRole> roles,
        CaptureEvent capture,
        bool creating,
        IReadOnlyDictionary<RepoRole, FolderCapturePlan>? plans = null,
        bool forksNewBranches = false) =>
        _services.Jobs.Start(
            JobKind.Capture,
            game.Id,
            cancellable: true,
            async context =>
            {
                var crowded = false;
                var result = await _services.RunForGameAsync(
                    game.Id,
                    async (storage, _) =>
                    {
                        // Whatever waited for the lock may have changed the game or removed the profile since the save was asked for.
                        var current = await _services.Catalog.GetGameAsync(game.Id, CancellationToken.None).ConfigureAwait(false)
                            ?? throw new GameNotFoundException(game.Id);
                        var target = creating
                            ? profile
                            : await _services.Catalog.GetProfileAsync(profile.Id, CancellationToken.None).ConfigureAwait(false) is { } found && found.GameId == game.Id
                                ? found
                                : throw new ProfileNotFoundException(game.Id, profile.Id);
                        // Checked again now that the lock is held: whatever waited for it may have switched a folder to another profile.
                        if (!creating && plans is null)
                        {
                            await RequireLoadedAsync(storage, current, target, roles, capture.Id, context.CancellationToken).ConfigureAwait(false);
                        }

                        var saved = await RunAsync(context, storage, current, target, pointName, roles, capture, creating, forksNewBranches || creating, plans).ConfigureAwait(false);

                        // Counted while the lock is held, only for a save nobody stopped (D13.17), so the job ends without waiting for the lock again.
                        crowded = !context.CancellationToken.IsCancellationRequested && saved.Folders.Any(folder => folder.Ok) && _optimization.IsCrowded(storage, current);
                        return saved;
                    },
                    context.CancellationToken).ConfigureAwait(false);
                Remember(game.Id, profile.Id, capture, result, forksNewBranches || creating);

                // Starting the tidy-up only schedules it; the save is already over.
                if (crowded)
                {
                    _optimization.Start(game.Id, OptimizationMode.Routine, preemptible: true);
                }

                return result;
            },
            new JobProfile(profile.Id, profile.Name));

    /// <summary>
    /// Refuses a plain save onto a branch that a folder is not on, before any folder is written: a snapshot copies what the folders hold, which
    /// is the loaded profile's files, so a profile the folders do not hold would be handed another profile's files. A folder the profile never saved is
    /// fine (its first snapshot there is what the folder holds), and so is a first save that stopped before the folder moved onto its branch, which the retry finishes.
    /// </summary>
    /// <param name="storage">The storage snapshot the caller holds, with the game's lock when the save is about to start.</param>
    /// <param name="game">The game.</param>
    /// <param name="profile">The profile the snapshot is for.</param>
    /// <param name="roles">The folders to be saved.</param>
    /// <param name="captureId">The id of the capture event the save belongs to, which tells a retry of a stopped first save from a new one.</param>
    /// <param name="cancellationToken">Cancels the check.</param>
    /// <exception cref="ProfileNotLoadedException">A folder is on another profile's branch.</exception>
    /// <exception cref="DetachedCaptureNotSupportedException">A folder is on an older snapshot.</exception>
    public async Task RequireLoadedAsync(StorageSnapshot storage, GameDefinition game, Profile profile, IReadOnlyList<RepoRole> roles, string? captureId, CancellationToken cancellationToken)
    {
        foreach (var repo in GameFolders.Repos(_services, storage, game).Where(repo => roles.Contains(repo.Role)))
        {
            if (!await storage.Engine.BranchExistsAsync(repo, profile.BranchName, cancellationToken).ConfigureAwait(false))
            {
                continue;
            }

            var head = GitDirFiles.ReadHead(repo.GitDir);
            if (SnapshotNowRule.Fits(true, head.IsDetached, head.Branch, profile.BranchName)
                || (captureId is not null && await HoldsOnlyAsync(storage, repo, profile.BranchName, captureId, cancellationToken).ConfigureAwait(false)))
            {
                continue;
            }

            throw head.IsDetached ? new DetachedCaptureNotSupportedException(repo.Role) : new ProfileNotLoadedException(profile.Id, repo.Role);
        }
    }

    /// <summary>The capture id of an earlier save of this profile that reached only some folders and could still be joined by a retry; null when there is none. Reads, takes nothing.</summary>
    /// <param name="gameId">The game.</param>
    /// <param name="profileId">The profile.</param>
    public string? PendingCaptureId(string gameId, string profileId) =>
        _partial.TryGetValue((gameId, profileId), out var partial) ? partial.Capture.Id : null;

    /// <summary>A new capture event: a fresh id, and now as the moment it began.</summary>
    public CaptureEvent NewEvent() => new(Guid.NewGuid().ToString("D"), _services.Time.GetUtcNow());

    /// <summary>
    /// The capture event a save of these folders belongs to: the earlier save that reached only some folders when the roles are
    /// exactly ones it missed (the retry joins that event, with its id and its time, and keeps its intent to branch from the folder
    /// when it was making a profile), a new event otherwise.
    /// </summary>
    /// <param name="gameId">The game.</param>
    /// <param name="profileId">The profile being saved.</param>
    /// <param name="roles">The folders about to be saved.</param>
    public (CaptureEvent Capture, bool ForksNewBranches) TakeEvent(string gameId, string profileId, IReadOnlyList<RepoRole> roles) =>
        _partial.TryRemove((gameId, profileId), out var partial) && roles.All(partial.Missing.Contains) ? (partial.Capture, partial.ForksNewBranches) : (NewEvent(), false);

    // A partial save is remembered; any other ending, including the retry that finished it, clears the memory.
    private void Remember(string gameId, string profileId, CaptureEvent capture, CaptureResult result, bool forksNewBranches)
    {
        var failed = result.Folders.Where(folder => !folder.Ok).Select(folder => folder.Role).ToList();
        if (failed.Count > 0 && failed.Count < result.Folders.Count)
        {
            _partial[(gameId, profileId)] = new PartialSave(capture, failed, forksNewBranches);
        }
        else
        {
            _partial.TryRemove((gameId, profileId), out _);
        }

        if (failed.Count == 0)
        {
            _judged.TryRemove(capture.Id, out _);
        }
    }

    private sealed record PartialSave(CaptureEvent Capture, IReadOnlyList<RepoRole> Missing, bool ForksNewBranches);

    private async Task<CaptureResult> RunAsync(
        JobContext context,
        StorageSnapshot storage,
        GameDefinition game,
        Profile profile,
        string pointName,
        IReadOnlyList<RepoRole> roles,
        CaptureEvent capture,
        bool creating,
        bool forksNewBranches,
        IReadOnlyDictionary<RepoRole, FolderCapturePlan>? plans)
    {
        var repos = GameFolders.Repos(_services, storage, game).Where(repo => roles.Contains(repo.Role)).ToList();
        plans = await RefuseUnsafeReplaceAsync(storage, game, profile, repos, plans, capture.Id, context.CancellationToken).ConfigureAwait(false);

        var progress = new CaptureProgressCombiner(context.Progress, context.AddLine);
        // A new profile's first snapshot carries the profile's name, so saying both would repeat it.
        if (creating && pointName == profile.Name)
        {
            context.AddStep(JobSteps.CaptureStartFirst, ("profile", profile.Name));
        }
        else
        {
            context.AddStep(JobSteps.CaptureStart, ("profile", profile.Name), ("snapshot", pointName));
        }


        // The folders are separate repositories, so they are saved side by side; one failing never stops the others. They write into one
        // storage volume, so each capture judges its own room first and then the volume is asked about the folders that fit alone, together.
        var room = repos.Count > 1 ? new CaptureRoomGate(storage.Engine, repos) : null;
        var saved = await Task.WhenAll(repos.Select(repo => SaveFolderAsync(context, storage, game, repo, profile.BranchName, pointName, capture, forksNewBranches, plans?.GetValueOrDefault(repo.Role), room, progress.For(repo.Role)))).ConfigureAwait(false);

        var outcomes = saved.Select(folder => folder.Outcome).ToList();
        if (creating)
        {
            await FinishCreationAsync(storage, profile, saved.Where(folder => folder.StartedBranch).Select(folder => folder.Repo), outcomes).ConfigureAwait(false);
        }

        var recorded = saved.Any(folder => folder.Recorded);
        return new CaptureResult(profile.Id, recorded ? new HistoryPointId(capture.Id) : null, outcomes);
    }

    // A replacement is judged again now, with the game's lock held and the folders as they are: what was true when the session ended may
    // not be (a folder changed since, another profile was made from the snapshot). When it no longer holds, every folder is saved as a new snapshot.
    private async Task<IReadOnlyDictionary<RepoRole, FolderCapturePlan>?> RefuseUnsafeReplaceAsync(
        StorageSnapshot storage,
        GameDefinition game,
        Profile profile,
        IReadOnlyList<RepoRef> repos,
        IReadOnlyDictionary<RepoRole, FolderCapturePlan>? plans,
        string captureId,
        CancellationToken cancellationToken)
    {
        if (plans is null || plans.Values.FirstOrDefault(plan => plan is { Mode: CaptureMode.Amend, AmendPointId: not null }) is not { AmendPointId: { } target })
        {
            return plans;
        }

        // A retry of the same save obeys the first judgment; judging only the folders that are left could turn a rewrite into a new snapshot or the reverse.
        if (_judged.TryGetValue(captureId, out var earlier))
        {
            return earlier;
        }

        var others = (await _services.Catalog.ListProfilesAsync(game.Id, cancellationToken).ConfigureAwait(false))
            .Where(listed => listed.Id != profile.Id)
            .Select(listed => listed.BranchName)
            .ToList();
        var judged = new Dictionary<RepoRole, ReplaceFolder>();
        foreach (var repo in repos.Where(repo => plans.GetValueOrDefault(repo.Role) is { Mode: CaptureMode.Amend }))
        {
            var plan = plans[repo.Role];
            var status = await storage.Engine.GetStatusAsync(repo, cancellationToken, waitForNote: false).ConfigureAwait(false);
            var leftAlone = plan.LeftAtLaunch && (status.IsDetached || !string.Equals(status.Branch, profile.BranchName, StringComparison.Ordinal))
                && await storage.Engine.BranchExistsAsync(repo, profile.BranchName, cancellationToken).ConfigureAwait(false);
            if (leftAlone || (status.IsClean && status.HeldFile is null))
            {
                continue;
            }

            judged[repo.Role] = await ReplaceRule.JudgeFolderAsync(storage.Engine, repo, profile.BranchName, others, target, cancellationToken).ConfigureAwait(false);
        }

        // Each folder's verdict goes into its plan: only a folder judged to be rewritten keeps its replacement, so a folder that is
        // written after this check, or that only joins, is saved as a plain commit and never rewrites anything.
        Dictionary<RepoRole, FolderCapturePlan> decided;
        if (ReplaceRule.Allows([.. judged.Values]))
        {
            decided = plans.ToDictionary(
                pair => pair.Key,
                pair => pair.Value.Mode == CaptureMode.Amend && judged.GetValueOrDefault(pair.Key, ReplaceFolder.Join) != ReplaceFolder.Amend ? pair.Value with { Mode = CaptureMode.Normal } : pair.Value);
        }
        else
        {
            _services.Log.Information($"Save: replacing snapshot {target} of {game.Id} was not safe any more, so the session is saved as a new snapshot.");
            decided = plans.ToDictionary(pair => pair.Key, pair => pair.Value.Mode == CaptureMode.Amend ? pair.Value with { Mode = CaptureMode.Normal, Downgraded = true } : pair.Value);
        }

        _judged[captureId] = decided;
        return decided;
    }

    private async Task<SavedFolder> SaveFolderAsync(
        JobContext context,
        StorageSnapshot storage,
        GameDefinition game,
        RepoRef repo,
        string branch,
        string pointName,
        CaptureEvent capture,
        bool forksNewBranches,
        FolderCapturePlan? plan,
        CaptureRoomGate? room,
        CaptureProgressCombiner.Part progress)
    {
        if (context.CancellationToken.IsCancellationRequested)
        {
            room?.DropOut(repo.Role);
            return new SavedFolder(repo, new FolderOutcome(repo.Role, ErrorInfo.From(new OperationCancelledByUserException(CancelledOperation.Capture, game.Id))), false, false);
        }

        var startedBranch = false;
        try
        {
            context.AddStep(JobSteps.CaptureFolder, ("folder", repo.Role), ("path", repo.WorkTree));
            var existed = await storage.Engine.BranchExistsAsync(repo, branch, context.CancellationToken).ConfigureAwait(false);
            startedBranch = !existed;
            var saved = await CaptureFolderAsync(storage, game, repo, branch, existed, pointName, capture, forksNewBranches, plan, room, progress, context.CancellationToken).ConfigureAwait(false);
            LogSkipped(game.Id, repo, branch, saved);
            if (saved.LeftAlone)
            {
                context.AddStep(JobSteps.CaptureFolderNotIncluded, ("folder", repo.Role));
            }
            else if (saved.SkippedAsClean)
            {
                context.AddStep(JobSteps.CaptureFolderSkipped, ("folder", repo.Role));
            }
            else
            {
                context.AddStep(JobSteps.CaptureFolderDone, ("folder", repo.Role), ("files", progress.FilesDone));
                if (saved.KeptAsNew)
                {
                    context.AddStep(JobSteps.CaptureFolderKeptAsNew, ("folder", repo.Role));
                }
            }

            return new SavedFolder(repo, new FolderOutcome(repo.Role, null, saved.LeftAlone, saved.LeftOnBranch, saved.KeptAsNew, saved.SkippedAsClean), saved.Recorded, startedBranch);
        }
        catch (Exception e) when (e is not (OutOfMemoryException or StackOverflowException))
        {
            // A stop the user asked for reads as a cancelled save, not as an unknown failure.
            if (e is not OperationCanceledException)
            {
                context.AddStep(JobSteps.CaptureFolderFailed, ("folder", repo.Role));
            }

            var error = e is OperationCanceledException ? new OperationCancelledByUserException(CancelledOperation.Capture, game.Id) : e;
            return new SavedFolder(repo, new FolderOutcome(repo.Role, ErrorInfo.From(error)), false, startedBranch);
        }
        finally
        {
            // A folder that ends without reporting must not keep the others waiting for it.
            room?.DropOut(repo.Role);

            // A folder with nothing to store never reports, and would otherwise hold the other folder's total back until the job ends.
            progress.Finish();
        }
    }

    // A folder a save did not write to leaves no point to find later, so the log says which one and why.
    private void LogSkipped(string gameId, RepoRef repo, string branch, FolderSave saved)
    {
        if (saved.LeftAlone)
        {
            _services.Log.Information($"Save: the {GameFolders.LogName(repo.Role)} folder of {gameId} ({repo.WorkTree}) was left alone: it sits on {saved.LeftOnBranch ?? "an older point"}, not on {branch}, so nothing was added to it.");
        }
        else if (saved.SkippedAsClean)
        {
            _services.Log.Information($"Save: the {GameFolders.LogName(repo.Role)} folder of {gameId} ({repo.WorkTree}) had nothing unsaved, so {branch} was not changed there.");
        }
    }

    // What a save did to one folder: whether it recorded a new point, or left the folder alone (and on which branch it sits), and whether a
    // replacement it was asked for became a new point instead.
    private readonly record struct FolderSave(bool Recorded, bool LeftAlone = false, string? LeftOnBranch = null, bool SkippedAsClean = false, bool KeptAsNew = false);

    private static async Task<FolderSave> CaptureFolderAsync(
        StorageSnapshot storage,
        GameDefinition game,
        RepoRef repo,
        string branch,
        bool branchExists,
        string pointName,
        CaptureEvent capture,
        bool forksNewBranch,
        FolderCapturePlan? plan,
        CaptureRoomGate? room,
        IProgress<FileProgress> progress,
        CancellationToken cancellationToken)
    {
        // A first save of this folder that stopped after its branch was written, before the folder moved onto it, is finished as it was begun.
        var finishing = branchExists
            && plan is null or { Mode: CaptureMode.Normal or CaptureMode.Root }
            && !string.Equals(GitDirFiles.ReadHead(repo.GitDir).Branch, branch, StringComparison.Ordinal)
            && await HoldsOnlyAsync(storage, repo, branch, capture.Id, cancellationToken).ConfigureAwait(false);

        if (plan is { SkipWhenClean: true } or { LeftAtLaunch: true })
        {
            var status = await storage.Engine.GetStatusAsync(repo, cancellationToken, waitForNote: false).ConfigureAwait(false);

            // Decided before anything is written, so a folder that cannot take the point is never a half-failed save.
            if (plan.LeftAtLaunch && branchExists && !finishing && (status.IsDetached || !string.Equals(status.Branch, branch, StringComparison.Ordinal)))
            {
                return new FolderSave(false, LeftAlone: true, status.IsDetached ? null : status.Branch);
            }

            // A file nobody can read is "cannot tell", not "unchanged": the save runs and says which file it could not read.
            if (plan.SkipWhenClean && status.IsClean && status.HeldFile is null)
            {
                return new FolderSave(false, SkippedAsClean: true);
            }
        }

        var mode = finishing ? CaptureMode.Root : await ModeOfAsync(storage, repo, branch, branchExists, forksNewBranch, plan, cancellationToken).ConfigureAwait(false);
        var request = new CaptureRequest(branch, pointName, capture.Id)
        {
            CapturedAt = capture.At,
            Mode = mode,
            IgnorePatterns = game.IgnorePathsFor(repo.Role),
            ExpectedStartCommit = plan?.ExpectedStartCommit,
            SkipAmendWhenUnchanged = plan?.SkipWhenClean ?? false,
            BeforeWriting = room is null ? null : (newBytes, token) => room.ReportAsync(repo.Role, newBytes, token),
        };
        var outcome = await storage.Engine.CaptureAsync(repo, request, progress, cancellationToken).ConfigureAwait(false);
        if (outcome.Changed || outcome.TipCommitId is not null)
        {
            // A replacement that found the snapshot was not the newest here was written as a new point, which the save reports.
            return new FolderSave(outcome.Changed, KeptAsNew: outcome.Changed && plan is { Downgraded: true });
        }

        // An empty folder in an empty repository has nothing to record; the profile still needs a branch here, and the folder is on it, as after any first save.
        await storage.Engine.CreateEmptyBranchAsync(repo, branch, pointName, capture.Id, capture.At, cancellationToken, attachToUnbornHead: true).ConfigureAwait(false);
        return new FolderSave(true);
    }

    // True when the branch is exactly one point, the one this capture event wrote: a point with nothing behind it, as a first save writes.
    private static async Task<bool> HoldsOnlyAsync(StorageSnapshot storage, RepoRef repo, string branch, string captureId, CancellationToken cancellationToken) =>
        await storage.Engine.ListCommitsAsync(repo, branch, 2, cancellationToken).ConfigureAwait(false) is [{ CaptureId: { } id }] && string.Equals(id, captureId, StringComparison.Ordinal);

    // A fork, a root or a move forward is as the session asked. Otherwise a new profile's branch starts from what the folder holds now, and
    // an existing profile's first save of a folder it never saved starts a history of its own instead of continuing another profile's.
    // An amend rewrites the newest commit, so it only runs where that commit is the point being replaced.
    private static async Task<CaptureMode> ModeOfAsync(StorageSnapshot storage, RepoRef repo, string branch, bool branchExists, bool forksNewBranch, FolderCapturePlan? plan, CancellationToken cancellationToken)
    {
        if (plan is { Mode: CaptureMode.Fork or CaptureMode.MoveForward or CaptureMode.Root })
        {
            return plan.Mode;
        }

        if (!branchExists)
        {
            return forksNewBranch ? CaptureMode.Fork : CaptureMode.Root;
        }

        if (plan is not { Mode: CaptureMode.Amend, AmendPointId: { } replaced })
        {
            return plan?.Mode ?? CaptureMode.Normal;
        }

        var newest = (await storage.Engine.ListCommitsAsync(repo, branch, 1, cancellationToken).ConfigureAwait(false)).FirstOrDefault();
        return string.Equals(newest?.CaptureId, replaced, StringComparison.Ordinal) ? CaptureMode.Amend : CaptureMode.Normal;
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
}
