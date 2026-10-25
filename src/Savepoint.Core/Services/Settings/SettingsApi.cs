using Savepoint.Core.Api;
using Savepoint.Core.Backend;
using Savepoint.Core.Catalog;
using Savepoint.Core.Git;
using Savepoint.Core.Services.History;
using Savepoint.Core.Services.Storage;

namespace Savepoint.Core.Services.Settings;

/// <summary>The real <see cref="ISettingsApi"/>: settings, storage summary, optimization, relocation and clear-all.</summary>
internal sealed partial class SettingsApi : ISettingsApi
{
    private readonly BackendServices _services;
    private readonly OptimizationService _optimization;
    private readonly StorageRelocator _relocator;
    private readonly DataClearer _clearer;

    public SettingsApi(BackendServices services, OptimizationService optimization, StorageRelocator relocator, DataClearer clearer)
    {
        _services = services;
        _optimization = optimization;
        _relocator = relocator;
        _clearer = clearer;
    }

    public Task<OperationResult<AppSettings>> GetAsync(CancellationToken cancellationToken = default) =>
        ApiGuard.RunAsync(_services.Log, async () => await StoredAsync(cancellationToken).ConfigureAwait(false), cancellationToken);

    // The root and the optimized date are the service's to change, so whatever the request says about them is ignored (D23).
    public Task<OperationResult<AppSettings>> SaveAsync(AppSettings settings) =>
        ApiGuard.RunAsync(_services.Log, () => _services.Catalog.UpdateSettingsAsync(current =>
            settings with
            {
                StorageRoot = current?.StorageRoot ?? _services.Platform.Directories.DefaultStorageRoot,
                LastOptimizedAt = current?.LastOptimizedAt,
            }));

    public Task<OperationResult<StorageSummary>> GetStorageSummaryAsync(CancellationToken cancellationToken = default) =>
        ApiGuard.RunAsync(
            _services.Log,
            async () =>
            {
                var games = await _services.Catalog.ListGamesAsync(cancellationToken).ConfigureAwait(false);
                var settings = await StoredAsync(cancellationToken).ConfigureAwait(false);
                return await _services.Storage.ReadAsync(
                    async storage =>
                    {
                        var sizes = new List<GameBytesUsed>();
                        foreach (var game in games)
                        {
                            sizes.Add(new GameBytesUsed(game.Id, await DirectorySize.GetBytesAsync(storage.GameDirectory(game.Id), cancellationToken).ConfigureAwait(false)));
                        }

                        return new StorageSummary(
                            storage.Root,
                            sizes.Sum(size => size.Bytes),
                            sizes,
                            _services.Disk.GetTotalBytes(storage.Root) ?? 0,
                            _services.Disk.GetFreeBytes(storage.Root) ?? 0,
                            settings.LastOptimizedAt,
                            StorageDestination.IsInInstallerFolder(_services, storage.Root, storage.PathSafety));
                    },
                    cancellationToken).ConfigureAwait(false);
            },
            cancellationToken);

    public Task<OperationResult<JobStarted>> OptimizeAllStorageAsync(FreeUpSpaceToken? confirmed = null) =>
        ApiGuard.RunAsync(_services.Log, () => Task.FromResult(new JobStarted(_optimization.Start(null, OptimizationMode.Reclaim, preemptible: false, confirmed, byRequest: true))));

    public Task<OperationResult<FreeUpSpacePreview>> GetFreeUpSpacePreviewAsync(CancellationToken cancellationToken = default) =>
        ApiGuard.RunAsync(
            _services.Log,
            async () =>
            {
                var games = await _services.Catalog.ListGamesAsync(cancellationToken).ConfigureAwait(false);
                var profiles = await _services.Catalog.ListProfilesAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
                return await _services.Storage.ReadAsync(
                    async storage =>
                    {
                        var total = 0;
                        var records = new List<FreeUpSpaceRecord>();
                        foreach (var game in games)
                        {
                            var preview = await RemovedSnapshots.PreviewAsync(storage, game.Id, GameFolders.ReposForGitDirOnly(_services, storage, game), [.. profiles.Where(profile => profile.GameId == game.Id)], cancellationToken).ConfigureAwait(false);
                            total += preview.RecentlyRemovedSnapshots;
                            records.AddRange(preview.Token.Records);
                        }

                        return new FreeUpSpacePreview(total, new FreeUpSpaceToken(records));
                    },
                    cancellationToken).ConfigureAwait(false);
            },
            cancellationToken);

    public Task<OperationResult<StorageRelocationPreview>> GetRelocationPreviewAsync(string newRoot, CancellationToken cancellationToken = default) =>
        ApiGuard.RunAsync(_services.Log, () => _relocator.PreviewAsync(newRoot, cancellationToken), cancellationToken);

    public Task<OperationResult<JobStarted>> RelocateStorageAsync(string newRoot) =>
        ApiGuard.RunAsync(_services.Log, async () => new JobStarted(await _relocator.StartAsync(newRoot).ConfigureAwait(false)));

    public Task<OperationResult<ClearAllDataPreview>> GetClearAllDataPreviewAsync(CancellationToken cancellationToken = default) =>
        ApiGuard.RunAsync(_services.Log, () => _clearer.PreviewAsync(cancellationToken), cancellationToken);

    public Task<OperationResult<JobStarted>> ClearAllDataAsync() =>
        ApiGuard.RunAsync(_services.Log, async () => new JobStarted(await _clearer.StartAsync().ConfigureAwait(false)));

    private async Task<AppSettings> StoredAsync(CancellationToken cancellationToken) =>
        await _services.Catalog.GetSettingsAsync(cancellationToken).ConfigureAwait(false)
        ?? AppSettings.Defaults(_services.Platform.Directories.DefaultStorageRoot);
}
