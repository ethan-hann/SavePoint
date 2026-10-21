using Savepoint.Core.Api;
using Savepoint.Core.Backend;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;
using Savepoint.Core.Git;
using Savepoint.Core.Services.Jobs;

namespace Savepoint.Core.Services.Storage;

/// <summary>How hard an optimization works (D28).</summary>
internal enum OptimizeMode
{
    /// <summary>Packs new objects and merges only small packs; runs by itself after captures and never rewrites a large pack.</summary>
    Routine,

    /// <summary>Drops everything no profile reaches and rewrites the packs; runs when the user asks and after a profile is deleted.</summary>
    Reclaim,
}

/// <summary>Starts and runs optimization jobs for one game or for every game.</summary>
internal sealed class OptimizationService
{
    // The same floor the engine keeps before it repacks.
    private const long _minFreeBytes = 100L * 1024 * 1024;

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
    public Guid Start(string? gameId, OptimizeMode mode, bool preemptible) =>
        _services.Jobs.Start(JobKind.Compact, gameId, cancellable: true, context => RunAsync(context, gameId, mode, preemptible));

    /// <summary>Starts a routine optimization for the game when either repository holds more loose objects than the threshold (D13.17).</summary>
    public async Task QueueRoutineIfNeededAsync(string gameId)
    {
        try
        {
            var crowded = await _services.RunForGameAsync(
                gameId,
                async (storage, _) =>
                {
                    if (await _services.Catalog.GetGameAsync(gameId).ConfigureAwait(false) is not { } game)
                    {
                        return false;
                    }

                    foreach (var repo in GameFolders.ReposForGitDirOnly(_services, storage, game))
                    {
                        if (await storage.Engine.CountLooseObjectsAsync(repo).ConfigureAwait(false) > _services.LooseObjectThreshold)
                        {
                            return true;
                        }
                    }

                    return false;
                }).ConfigureAwait(false);
            if (crowded)
            {
                Start(gameId, OptimizeMode.Routine, preemptible: true);
            }
        }
        catch (Exception e) when (e is SavepointException or IOException)
        {
            // The check only decides whether to tidy up; a failure of it must not fail the save it follows.
        }
    }

    private async Task<JobResult?> RunAsync(JobContext context, string? gameId, OptimizeMode mode, bool preemptible)
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
                outcomes.Add(await OptimizeGameAsync(context, game, mode, preemptible, () => preempted = true).ConfigureAwait(false));
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

        await MarkOptimizedAsync(outcomes).ConfigureAwait(false);
        return new OptimizeResult(outcomes);
    }

    private Task<GameOptimizeOutcome> OptimizeGameAsync(JobContext context, GameDefinition game, OptimizeMode mode, bool preemptible, Action onPreempted) =>
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

                var repos = GameFolders.ReposForGitDirOnly(_services, storage, game);
                var before = await SizeOfAsync(repos, context.CancellationToken).ConfigureAwait(false);
                var folders = new List<FolderOutcome>();
                foreach (var repo in repos)
                {
                    context.CancellationToken.ThrowIfCancellationRequested();
                    try
                    {
                        await OptimizeRepoAsync(storage, repo, mode, context.CancellationToken).ConfigureAwait(false);
                        folders.Add(new FolderOutcome(repo.Role));
                    }
                    catch (Exception e) when (e is SavepointException and not OperationCancelledByUserException)
                    {
                        folders.Add(new FolderOutcome(repo.Role, ErrorInfo.From(e)));
                    }
                }

                var after = await SizeOfAsync(repos, context.CancellationToken).ConfigureAwait(false);
                return new GameOptimizeOutcome(game.Id, folders, folders.Any(folder => folder.Ok) ? Math.Max(0, before - after) : null);
            },
            context.CancellationToken);

    // The second pass is the one that frees space; it only runs after the first has left nothing but packs.
    private async Task OptimizeRepoAsync(StorageSnapshot storage, RepoRef repo, OptimizeMode mode, CancellationToken cancellationToken)
    {
        if (mode == OptimizeMode.Routine)
        {
            await storage.Engine.RepackAsync(repo, cancellationToken).ConfigureAwait(false);
            return;
        }

        var free = _services.Disk.GetFreeBytes(repo.GitDir);
        if (free is { } known && known < _minFreeBytes)
        {
            throw new DiskSpaceException(repo.GitDir, _minFreeBytes, known);
        }

        // Reflogs would keep every dropped point reachable; SavePoint never reads them.
        await RunGitAsync(repo, cancellationToken, "reflog", "expire", "--expire=now", "--all").ConfigureAwait(false);
        await RunGitAsync(repo, cancellationToken, "repack", "-a", "-d", "-q").ConfigureAwait(false);
        await RunGitAsync(repo, cancellationToken, "prune", "--expire=now").ConfigureAwait(false);
    }

    private async Task RunGitAsync(RepoRef repo, CancellationToken cancellationToken, params string[] arguments)
    {
        try
        {
            await _services.Runner.RunAsync(new GitInvocation(arguments) { GitDir = repo.GitDir, WorkingDirectory = repo.GitDir }, cancellationToken).ConfigureAwait(false);
        }
        catch (GitCommandException e)
        {
            throw new GitOperationException(null, e.Message);
        }
    }

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
