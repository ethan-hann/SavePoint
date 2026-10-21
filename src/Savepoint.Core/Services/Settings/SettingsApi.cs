using Savepoint.Core.Api;
using Savepoint.Core.Backend;
using Savepoint.Core.Catalog;
using Savepoint.Core.Services.Storage;

namespace Savepoint.Core.Services.Settings;

/// <summary>The real <see cref="ISettingsApi"/>: settings, storage summary, optimization, relocation and clear-all.</summary>
internal sealed partial class SettingsApi : ISettingsApi
{
    private readonly BackendServices _services;
    private readonly OptimizationService _optimization;

    public SettingsApi(BackendServices services, OptimizationService optimization)
    {
        _services = services;
        _optimization = optimization;
    }

    public Task<OperationResult<AppSettings>> GetAsync(CancellationToken cancellationToken = default) =>
        ApiGuard.NotBuilt<AppSettings>();

    public Task<OperationResult<AppSettings>> SaveAsync(AppSettings settings) => ApiGuard.NotBuilt<AppSettings>();

    public Task<OperationResult<StorageSummary>> GetStorageSummaryAsync(CancellationToken cancellationToken = default) =>
        ApiGuard.NotBuilt<StorageSummary>();

    public Task<OperationResult<JobStarted>> OptimizeAllStorageAsync() => ApiGuard.NotBuilt<JobStarted>();

    public Task<OperationResult<StorageRelocationPreview>> GetRelocationPreviewAsync(string newRoot, CancellationToken cancellationToken = default) =>
        ApiGuard.NotBuilt<StorageRelocationPreview>();

    public Task<OperationResult<JobStarted>> RelocateStorageAsync(string newRoot) => ApiGuard.NotBuilt<JobStarted>();

    public Task<OperationResult<ClearAllDataPreview>> GetClearAllDataPreviewAsync(CancellationToken cancellationToken = default) =>
        ApiGuard.NotBuilt<ClearAllDataPreview>();

    public Task<OperationResult<JobStarted>> ClearAllDataAsync() => ApiGuard.NotBuilt<JobStarted>();
}
