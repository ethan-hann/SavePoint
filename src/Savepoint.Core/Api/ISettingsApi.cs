using Savepoint.Core.Catalog;

namespace Savepoint.Core.Api;

public interface ISettingsApi
{
    Task<OperationResult<AppSettings>> GetAsync(CancellationToken cancellationToken = default);

    // Keeps the stored StorageRoot and LastOptimizedAt: storage moves through RelocateStorageAsync and the service owns the date.
    Task<OperationResult<AppSettings>> SaveAsync(AppSettings settings);

    Task<OperationResult<StorageSummary>> GetStorageSummaryAsync(CancellationToken cancellationToken = default);

    // Settings' "Run now": one job across every game.
    Task<OperationResult<JobStarted>> OptimizeAllStorageAsync();

    Task<OperationResult<StorageRelocationPreview>> GetRelocationPreviewAsync(string newRoot, CancellationToken cancellationToken = default);

    Task<OperationResult<JobStarted>> RelocateStorageAsync(string newRoot);

    Task<OperationResult<ClearAllDataPreview>> GetClearAllDataPreviewAsync(CancellationToken cancellationToken = default);

    // Not cancellable: stopping halfway would leave some games cleared and others not (D8).
    Task<OperationResult<JobStarted>> ClearAllDataAsync();
}
