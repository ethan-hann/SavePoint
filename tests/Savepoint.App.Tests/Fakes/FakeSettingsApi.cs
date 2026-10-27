using Savepoint.Core.Api;
using Savepoint.Core.Catalog;

namespace Savepoint.App.Tests.Fakes;

public sealed class FakeSettingsApi : ISettingsApi
{
    private const string _api = nameof(ISettingsApi);

    private readonly FakeBackend _backend;

    internal FakeSettingsApi(FakeBackend backend)
    {
        _backend = backend;
    }

    private FakeState State => _backend.State;

    public Task<OperationResult<AppSettings>> GetAsync(CancellationToken cancellationToken = default) =>
        _backend.Call(_api, nameof(GetAsync), [], () => OperationResult.Success(State.Settings));

    // A test sets this to hold the next save, as a slow disk would.
    public Func<Task>? SaveGate { get; set; }

    public async Task<OperationResult<AppSettings>> SaveAsync(AppSettings settings)
    {
        if (SaveGate is { } gate)
        {
            SaveGate = null;
            await gate();
        }

        return await _backend.Call(_api, nameof(SaveAsync), [settings], () =>
        {
            State.Settings = settings with { StorageRoot = State.Settings.StorageRoot, LastOptimizedAt = State.Settings.LastOptimizedAt };
            return OperationResult.Success(State.Settings);
        });
    }

    public Task<OperationResult<StorageSummary>> GetStorageSummaryAsync(CancellationToken cancellationToken = default) =>
        _backend.Call(_api, nameof(GetStorageSummaryAsync), [], () => OperationResult.Success(new StorageSummary(
            State.Settings.StorageRoot,
            State.TotalBytes,
            GamesBytes(),
            State.VolumeTotalBytes,
            State.VolumeFreeBytes,
            State.Settings.LastOptimizedAt,
            State.StorageInInstallerFolder)));

    public Task<OperationResult<JobStarted>> OptimizeAllStorageAsync(FreeUpSpaceToken? confirmed = null) =>
        _backend.Call(_api, nameof(OptimizeAllStorageAsync), [confirmed], () =>
        {
            var done = new OptimizeResult([.. State.Games.Select(Optimized)]);
            var opening = State.Games.Select(game => JobLine.ForStep(JobSteps.CompactGame, ("game", game.Name))).ToList();

            // Once the job has run, the recently removed snapshots the player agreed to lose are gone for good and no others, as in the real backend.
            return OperationResult.Success(new JobStarted(_backend.Jobs.Start(
                JobKind.Compact,
                null,
                done,
                job =>
                {
                    if (job.Status is JobStatus.Succeeded or JobStatus.Partial)
                    {
                        State.DeleteRemoved(confirmed);
                    }

                    MarkOptimized(job);
                },
                opening)));
        });

    public Task<OperationResult<FreeUpSpacePreview>> GetFreeUpSpacePreviewAsync(CancellationToken cancellationToken = default) =>
        _backend.Call(_api, nameof(GetFreeUpSpacePreviewAsync), [], () => OperationResult.Success(State.FreeUpPreview(null)));

    public Task<OperationResult<StorageRelocationPreview>> GetRelocationPreviewAsync(string newRoot, CancellationToken cancellationToken = default) =>
        _backend.Call(_api, nameof(GetRelocationPreviewAsync), [newRoot], () => OperationResult.Success(State.RelocationPreview ?? new StorageRelocationPreview(
            State.Settings.StorageRoot,
            newRoot,
            GamesBytes(),
            State.TotalBytes,
            State.CapturePreviews.Values.Sum(p => p.Folders.Sum(f => f.FileCount)),
            State.VolumeFreeBytes,
            State.RunningGameError())));

    // The settings follow the move only once it has finished; a cancelled or failed move leaves them alone.
    public Task<OperationResult<JobStarted>> RelocateStorageAsync(string newRoot) =>
        _backend.Call(_api, nameof(RelocateStorageAsync), [newRoot], () =>
        {
            if (State.RunningGameError() is { } running)
            {
                return OperationResult.Failure<JobStarted>(running);
            }

            var done = new StorageRelocationResult(State.Settings.StorageRoot, newRoot, State.TotalBytes, []);
            var opening = JobLine.ForStep(
                JobSteps.RelocateStart,
                ("from", State.Settings.StorageRoot),
                ("to", newRoot),
                ("files", State.CapturePreviews.Values.Sum(p => p.Folders.Sum(f => f.FileCount))),
                ("bytes", State.TotalBytes));
            var jobId = _backend.Jobs.Start(
                JobKind.RelocateStorage,
                null,
                done,
                job =>
                {
                    if (job.Status is JobStatus.Succeeded or JobStatus.Partial)
                    {
                        State.Settings = State.Settings with { StorageRoot = newRoot };
                    }
                },
                [opening]);
            return OperationResult.Success(new JobStarted(jobId));
        });

    public Task<OperationResult<ClearAllDataPreview>> GetClearAllDataPreviewAsync(CancellationToken cancellationToken = default) =>
        _backend.Call(_api, nameof(GetClearAllDataPreviewAsync), [], () => OperationResult.Success(State.ClearAllDataPreview ?? new ClearAllDataPreview(
            State.Settings.StorageRoot,
            State.TotalBytes,
            State.Games.Count,
            State.Profiles.Count,
            State.CatalogDirectory,
            [.. State.Games.SelectMany(g => FakeState.FoldersOf(g).Select(r => State.Resolve(FakeGamesApi.PathOf(g, r))))],
            State.RunningGameError())));

    public Task<OperationResult<JobStarted>> ClearAllDataAsync() =>
        _backend.Call(_api, nameof(ClearAllDataAsync), [], () =>
        {
            if (State.RunningGameError() is { } running)
            {
                return OperationResult.Failure<JobStarted>(running);
            }

            var done = new ClearAllDataResult(State.TotalBytes, []);
            var jobId = _backend.Jobs.Start(
                JobKind.ClearData,
                null,
                done,
                job =>
                {
                    if (job.Status is JobStatus.Succeeded or JobStatus.Partial)
                    {
                        State.ClearLibrary();
                    }
                },
                [JobLine.ForStep(JobSteps.ClearStart, ("games", State.Games.Count))]);
            return OperationResult.Success(new JobStarted(jobId));
        });

    internal static GameOptimizeOutcome Optimized(GameDefinition game) =>
        new(game.Id, [.. FakeState.FoldersOf(game).Select(r => new FolderOutcome(r))], null);

    // The date moves when anything was optimized, which a partial run still did.
    internal void MarkOptimized(JobSnapshot job)
    {
        if (job.Status is JobStatus.Succeeded or JobStatus.Partial)
        {
            State.Settings = State.Settings with { LastOptimizedAt = _backend.Clock.Now };
        }
    }

    private IReadOnlyList<GameBytesUsed> GamesBytes() => [.. State.Games.Select(g => new GameBytesUsed(g.Id, State.GameBytes.GetValueOrDefault(g.Id)))];
}
