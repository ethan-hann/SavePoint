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
    public Guid Start(string? gameId, OptimizationMode mode, bool preemptible) =>
        _services.Jobs.Start(JobKind.Compact, gameId, cancellable: true, context => RunAsync(context, gameId, mode, preemptible));

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

    private async Task<JobResult?> RunAsync(JobContext context, string? gameId, OptimizationMode mode, bool preemptible)
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

    private Task<GameOptimizeOutcome> OptimizeGameAsync(JobContext context, GameDefinition game, OptimizationMode mode, bool preemptible, Action onPreempted) =>
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
                        context.AddStep(JobSteps.CompactFolderFailed, ("game", game.Name), ("folder", repo.Role));
                        folders.Add(new FolderOutcome(repo.Role, ErrorInfo.From(e)));
                    }
                }

                var after = await SizeOfAsync(repos, context.CancellationToken).ConfigureAwait(false);
                var freed = folders.Any(folder => folder.Ok) ? Math.Max(0, before - after) : (long?)null;
                if (freed is { } bytes)
                {
                    context.AddStep(bytes > 0 ? JobSteps.CompactFreed : JobSteps.CompactNothingFreed, bytes > 0 ? [("game", game.Name), ("bytes", bytes)] : [("game", game.Name)]);
                }

                return new GameOptimizeOutcome(game.Id, folders, freed);
            },
            context.CancellationToken);

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
