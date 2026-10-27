using Savepoint.Core.Api;
using Savepoint.Core.Catalog;

namespace Savepoint.App.Tests.Features.EndToEnd;

// Forwards to the fake but can hold back the next save, so a test decides when a slow write lands and who else writes meanwhile.
internal sealed class GatedSettingsApi(ISettingsApi inner) : ISettingsApi
{
    private readonly TaskCompletionSource _heldDone = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private TaskCompletionSource? _armed;
    private TaskCompletionSource? _waiting;

    // Completes once the save that was held has reached the backend.
    public Task HeldSaveDone => _heldDone.Task;

    // The next save waits, with the settings it was called with, until ReleaseSaves; later saves go straight through.
    public void HoldNextSave() => _armed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

    public void ReleaseSaves()
    {
        Interlocked.Exchange(ref _armed, null)?.TrySetResult();
        Interlocked.Exchange(ref _waiting, null)?.TrySetResult();
    }

    public Task<OperationResult<AppSettings>> GetAsync(CancellationToken cancellationToken = default) => inner.GetAsync(cancellationToken);

    public async Task<OperationResult<AppSettings>> SaveAsync(AppSettings settings)
    {
        var gate = Interlocked.Exchange(ref _armed, null);
        if (gate is null)
        {
            return await inner.SaveAsync(settings);
        }

        _waiting = gate;
        await gate.Task;
        var saved = await inner.SaveAsync(settings);
        _heldDone.TrySetResult();
        return saved;
    }

    public Task<OperationResult<StorageSummary>> GetStorageSummaryAsync(CancellationToken cancellationToken = default) => inner.GetStorageSummaryAsync(cancellationToken);

    public Task<OperationResult<JobStarted>> OptimizeAllStorageAsync(FreeUpSpaceToken? confirmed = null) => inner.OptimizeAllStorageAsync(confirmed);

    public Task<OperationResult<FreeUpSpacePreview>> GetFreeUpSpacePreviewAsync(CancellationToken cancellationToken = default) =>
        inner.GetFreeUpSpacePreviewAsync(cancellationToken);

    public Task<OperationResult<long>> EstimateFreeUpSpaceAsync(FreeUpSpaceToken token, CancellationToken cancellationToken = default) =>
        inner.EstimateFreeUpSpaceAsync(token, cancellationToken);

    public Task<OperationResult<StorageRelocationPreview>> GetRelocationPreviewAsync(string newRoot, CancellationToken cancellationToken = default) =>
        inner.GetRelocationPreviewAsync(newRoot, cancellationToken);

    public Task<OperationResult<JobStarted>> RelocateStorageAsync(string newRoot) => inner.RelocateStorageAsync(newRoot);

    public Task<OperationResult<ClearAllDataPreview>> GetClearAllDataPreviewAsync(CancellationToken cancellationToken = default) =>
        inner.GetClearAllDataPreviewAsync(cancellationToken);

    public Task<OperationResult<JobStarted>> ClearAllDataAsync() => inner.ClearAllDataAsync();
}
