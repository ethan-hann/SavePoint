using Savepoint.Core.Api;
using Savepoint.Core.Backend;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;
using Savepoint.Core.Git;
using Savepoint.Core.Services.Jobs;

namespace Savepoint.Core.Services.Storage;

/// <summary>Starts and runs optimization jobs for one game or for every game.</summary>
internal sealed class OptimizationService
{
    private readonly BackendServices _services;

    public OptimizationService(BackendServices services)
    {
        _services = services;
    }

    /// <summary>
    /// Starts an optimization job.
    /// </summary>
    /// <param name="gameId">The game to optimize, or null for every game, one after the other.</param>
    /// <param name="mode">How hard to work.</param>
    /// <param name="preemptible">True when a launch, capture or delete on the same game may stop it to go first (D13.17).</param>
    /// <param name="confirmed">The recently removed snapshots' records the run may delete, from a preview the player agreed to; null deletes none.</param>
    /// <param name="byRequest">True for the Free up space the player asked for, the only run that moves the date Settings shows; the cleanups SavePoint starts by itself leave it alone.</param>
    public Guid Start(string? gameId, OptimizationMode mode, bool preemptible, FreeUpSpaceToken? confirmed = null, bool byRequest = false) =>
        _services.Jobs.Start(JobKind.Compact, gameId, cancellable: true, context => RunAsync(context, gameId, mode, preemptible, confirmed ?? FreeUpSpaceToken.Nothing, byRequest));

    /// <summary>True when either repository of the game holds more loose objects than the threshold, which is when a save queues a routine optimization (D13.17).</summary>
    /// <param name="storage">The storage snapshot the caller holds the game's lock for.</param>
    /// <param name="game">The game.</param>
    public bool IsCrowded(StorageSnapshot storage, GameDefinition game)
    {
        try
        {
            return GameFolders.ReposForGitDirOnly(_services, storage, game).Any(repo => CountLooseObjects(repo.GitDir) > _services.LooseObjectThreshold);
        }
        catch (Exception e) when (e is SavepointException or IOException)
        {
            // The count only decides whether to tidy up; a failure of it must not fail the save it follows.
            return false;
        }
    }

    // Loose objects sit in two-character fan-out folders under objects/; counting the files there costs no git process.
    internal static int CountLooseObjects(string gitDir)
    {
        var objects = Path.Combine(gitDir, "objects");
        if (!Directory.Exists(objects))
        {
            return 0;
        }

        try
        {
            return Directory.EnumerateDirectories(objects)
                .Where(folder => Path.GetFileName(folder).Length == 2)
                .Sum(folder => Directory.EnumerateFiles(folder).Count());
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return 0;
        }
    }

    private async Task<JobResult?> RunAsync(JobContext context, string? gameId, OptimizationMode mode, bool preemptible, FreeUpSpaceToken confirmed, bool byRequest)
    {
        var games = gameId is null
            ? await _services.Catalog.ListGamesAsync(context.CancellationToken).ConfigureAwait(false)
            : (await _services.Catalog.GetGameAsync(gameId, context.CancellationToken).ConfigureAwait(false)) is { } one ? [one] : [];

        var preempted = false;
        var outcomes = new List<GameOptimizeOutcome>();
        try
        {
            foreach (var game in games)
            {
                context.CancellationToken.ThrowIfCancellationRequested();
                outcomes.Add(await OptimizeGameAsync(context, game, mode, preemptible, confirmed, () => preempted = true).ConfigureAwait(false));
            }
        }
        catch (Exception e) when (e is OperationCanceledException or OperationCancelledByUserException)
        {
            // The job's own exception decides which message the registry shows: a preemption is not something the user asked for.
            if (preempted)
            {
                throw new CompactionPreemptedException(gameId);
            }

            if (e is OperationCancelledByUserException)
            {
                throw;
            }

            throw new OperationCancelledByUserException(CancelledOperation.Compaction, gameId);
        }

        if (byRequest)
        {
            await MarkOptimizedAsync(outcomes).ConfigureAwait(false);
        }

        return new OptimizeResult(outcomes);
    }

    private Task<GameOptimizeOutcome> OptimizeGameAsync(JobContext context, GameDefinition game, OptimizationMode mode, bool preemptible, FreeUpSpaceToken confirmed, Action onPreempted) =>
        _services.RunForGameAsync(
            game.Id,
            async (storage, handle) =>
            {
                using var registration = preemptible
                    ? handle.MarkPreemptible(() =>
                    {
                        onPreempted();
                        _services.Jobs.Preempt(context.JobId);
                    })
                    : null;

                context.AddStep(JobSteps.CompactGame, ("game", game.Name));

                // A free-up that was cut off earlier has deleted some of its records and not others; it is finished before anything else is judged.
                await FreeUpIntent.FinishAsync(storage, game.Id, context.CancellationToken).ConfigureAwait(false);
                var repos = GameFolders.ReposForGitDirOnly(_services, storage, game);
                var before = await SizeOfAsync(repos, context.CancellationToken).ConfigureAwait(false);

                // Only the records the player was told about go; one written since stays and keeps its snapshot alive through the reclaim.
                // A branch a profile names is never among them, whatever the token says.
                var profileBranches = (await _services.Catalog.ListProfilesAsync(game.Id).ConfigureAwait(false)).Select(profile => GitDirFiles.BranchPrefix + profile.BranchName).ToHashSet(StringComparer.Ordinal);
                var records = repos.ToDictionary(
                    repo => repo.Role,
                    repo => confirmed.Records
                        .Where(record => record.GameId == game.Id && record.Folder == repo.Role && !profileBranches.Contains(record.RefName))
                        .Select(record => new ClearedRef(record.RefName, record.Value))
                        .ToList());
                var (folders, pastTheClear) = records.Values.Any(names => names.Count > 0)
                    ? await FreeUpGameAsync(context, game, storage, repos, records, mode).ConfigureAwait(false)
                    : (await ReclaimFoldersAsync(context, game, storage, repos, mode, context.CancellationToken).ConfigureAwait(false), false);

                // A run that deleted records is past stopping, so measuring what it freed ignores the job's token.
                var after = await SizeOfAsync(repos, pastTheClear ? CancellationToken.None : context.CancellationToken).ConfigureAwait(false);
                var freed = folders.Any(folder => folder.Ok) ? Math.Max(0, before - after) : (long?)null;
                if (freed is { } bytes)
                {
                    context.AddStep(bytes > 0 ? JobSteps.CompactFreed : JobSteps.CompactNothingFreed, bytes > 0 ? [("game", game.Name), ("bytes", bytes)] : [("game", game.Name)]);
                }

                return new GameOptimizeOutcome(game.Id, folders, freed);
            },
            context.CancellationToken);

    // Each folder on its own: one that refuses or fails is named, and the others still go.
    private static async Task<List<FolderOutcome>> ReclaimFoldersAsync(JobContext context, GameDefinition game, StorageSnapshot storage, IReadOnlyList<RepoRef> repos, OptimizationMode mode, CancellationToken cancellationToken)
    {
        var folders = new List<FolderOutcome>();
        foreach (var repo in repos)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                await OptimizeRepoAsync(storage, repo, mode, cancellationToken).ConfigureAwait(false);
                folders.Add(new FolderOutcome(repo.Role));
            }
            catch (Exception e) when (e is SavepointException and not OperationCancelledByUserException)
            {
                context.AddStep(JobSteps.CompactFolderFailed, ("game", game.Name), ("folder", repo.Role));
                folders.Add(new FolderOutcome(repo.Role, ErrorInfo.From(e)));
            }
        }

        return folders;
    }

    // A game's recently removed snapshots are one unit across its folders. Every folder is judged before any record goes, so a folder that
    // refuses leaves all of them as they were; what is about to go is written down, and the records of every folder then go together or
    // are put back, and a run cut off in between is finished by the next start from what was written down; and from there the run
    // finishes whatever is asked of it, because the snapshots it deleted cannot be brought back and a stop would only leave the space
    // unfreed under a message saying nothing was lost.
    private static async Task<(List<FolderOutcome> Folders, bool PastTheClear)> FreeUpGameAsync(
        JobContext context,
        GameDefinition game,
        StorageSnapshot storage,
        IReadOnlyList<RepoRef> repos,
        IReadOnlyDictionary<RepoRole, List<ClearedRef>> records,
        OptimizationMode mode)
    {
        var refusals = new Dictionary<RepoRole, ErrorInfo>();
        foreach (var repo in repos)
        {
            try
            {
                await storage.Engine.CheckReclaimAsync(repo, context.CancellationToken).ConfigureAwait(false);
            }
            catch (Exception e) when (e is SavepointException and not OperationCancelledByUserException)
            {
                context.AddStep(JobSteps.CompactFolderFailed, ("game", game.Name), ("folder", repo.Role));
                refusals[repo.Role] = ErrorInfo.From(e);
            }
        }

        if (refusals.Count > 0)
        {
            var first = refusals.Values.First();
            return ([.. repos.Select(repo => new FolderOutcome(repo.Role, refusals.GetValueOrDefault(repo.Role) ?? first))], false);
        }

        // Marked first, so a stop request either arrived before this point, when the run ends with nothing deleted, or is refused. The token
        // alone could miss a stop that was accepted a moment ago, so the registry says whether one was.
        if (context.NoLongerCancellable())
        {
            throw new OperationCanceledException(context.CancellationToken);
        }

        // What each folder will delete, with the values, written down before the first record goes. A ref that no longer holds the value
        // the preview read (a branch that moved on) was not what the player agreed to lose, so it is left out.
        var plans = new Dictionary<RepoRole, IReadOnlyList<ClearedRef>>();
        foreach (var repo in repos.Where(repo => records[repo.Role].Count > 0))
        {
            var found = await storage.Engine.FindClearableAsync(repo, [.. records[repo.Role].Select(record => record.RefName)], CancellationToken.None).ConfigureAwait(false);
            plans[repo.Role] = [.. found.Where(records[repo.Role].Contains)];
        }

        var gameDirectory = storage.GameDirectory(game.Id);
        if (plans.Values.Any(refs => refs.Count > 0))
        {
            FreeUpIntent.Write(gameDirectory, plans);
        }

        var cleared = new List<(RepoRef Repo, IReadOnlyList<ClearedRef> Refs)>();
        try
        {
            foreach (var repo in repos.Where(repo => plans.ContainsKey(repo.Role)))
            {
                cleared.Add((repo, await storage.Engine.ClearExactAsync(repo, plans[repo.Role], CancellationToken.None).ConfigureAwait(false)));
            }
        }
        catch
        {
            // A folder that could not clear its records gives back the other's, so no snapshot is left with only some of its folders. When
            // that fails too, the note stays and the next start finishes the clear instead.
            if (await RestoreAllQuietlyAsync(storage, cleared).ConfigureAwait(false))
            {
                FreeUpIntent.Clear(gameDirectory);
            }

            throw;
        }

        FreeUpIntent.Clear(gameDirectory);
        if (cleared.Sum(folder => folder.Refs.Count) > 0)
        {
            context.AddStep(JobSteps.CompactRemovedDeleted, ("game", game.Name));
        }

        return (await ReclaimFoldersAsync(context, game, storage, repos, mode, CancellationToken.None).ConfigureAwait(false), true);
    }

    // Best effort: the failure being reported is the one worth showing. True when every folder has its records back.
    private static async Task<bool> RestoreAllQuietlyAsync(StorageSnapshot storage, IReadOnlyList<(RepoRef Repo, IReadOnlyList<ClearedRef> Refs)> cleared)
    {
        var allBack = true;
        foreach (var (repo, refs) in cleared)
        {
            try
            {
                await storage.Engine.RestoreRemovedAsync(repo, refs, CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception e) when (e is SavepointException or IOException)
            {
                allBack = false;
            }
        }

        return allBack;
    }

    // The engine keeps the free-space floor itself and refuses a reclaim that needs room the volume lacks.
    private static Task OptimizeRepoAsync(StorageSnapshot storage, RepoRef repo, OptimizationMode mode, CancellationToken cancellationToken) =>
        storage.Engine.RepackAsync(repo, mode, cancellationToken);

    private static async Task<long> SizeOfAsync(IEnumerable<RepoRef> repos, CancellationToken cancellationToken)
    {
        var total = 0L;
        foreach (var repo in repos)
        {
            total += await DirectorySize.GetBytesAsync(repo.GitDir, cancellationToken).ConfigureAwait(false);
        }

        return total;
    }

    // The date moves when anything was optimized, which a partial run still did.
    private async Task MarkOptimizedAsync(IReadOnlyList<GameOptimizeOutcome> outcomes)
    {
        if (!outcomes.Any(game => game.Folders.Any(folder => folder.Ok)))
        {
            return;
        }

        try
        {
            var now = _services.Time.GetUtcNow();
            await _services.Catalog.UpdateSettingsAsync(
                current => (current ?? AppSettings.Defaults(_services.Platform.Directories.DefaultStorageRoot)) with { LastOptimizedAt = now }).ConfigureAwait(false);
        }
        catch (CatalogCorruptException)
        {
            // The date is a courtesy; an unreadable settings file is reported by the startup check, not by this job.
        }
    }
}
