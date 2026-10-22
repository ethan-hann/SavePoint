using Savepoint.Core.Api;
using Savepoint.Core.Errors;

namespace Savepoint.App.Tests.Fakes;

public sealed class FakeAppStatusApi : IAppStatusApi
{
    private const string _api = nameof(IAppStatusApi);

    private readonly FakeBackend _backend;

    internal FakeAppStatusApi(FakeBackend backend)
    {
        _backend = backend;
    }

    private FakeState State => _backend.State;

    public Task<OperationResult<StartupStatus>> CheckAsync(CancellationToken cancellationToken = default) =>
        _backend.Call(_api, nameof(CheckAsync), [], () => OperationResult.Success(State.Startup));

    public Task<OperationResult<string>> RecoverCatalogFileAsync(string path, CatalogRecovery recovery, CancellationToken cancellationToken = default) =>
        _backend.Call(_api, nameof(RecoverCatalogFileAsync), [path, recovery], () =>
        {
            var catalog = State.Startup.Catalog;
            var file = catalog.CorruptFiles.FirstOrDefault(f => f.Path == path)
                ?? throw new KeyNotFoundException($"'{path}' is not listed as corrupt.");
            if (recovery == CatalogRecovery.RestoreBackup && file.BackupPath is null)
            {
                throw new InvalidOperationException($"'{path}' has no backup to restore.");
            }

            State.Startup = State.Startup with { Catalog = catalog with { CorruptFiles = [.. catalog.CorruptFiles.Where(f => f != file)] } };
            return OperationResult.Success($"{path}.corrupt");
        });

    // As the real one: only a folder that holds the history of the games is taken, and nothing changes for any other.
    public Task<OperationResult<string>> UseStorageFolderAsync(string folder, CancellationToken cancellationToken = default) =>
        _backend.Call(_api, nameof(UseStorageFolderAsync), [folder], () =>
            State.StorageFoldersWithHistory.Contains(folder)
                ? Reachable(folder)
                : OperationResult.Failure<string>(new StorageHistoryNotFoundException(folder)));

    // As the real one: history already in the default folder is not an empty start.
    public Task<OperationResult<string>> StartWithEmptyStorageAsync(CancellationToken cancellationToken = default) =>
        _backend.Call(_api, nameof(StartWithEmptyStorageAsync), [], () =>
            State.StorageFoldersWithHistory.Contains(State.DefaultStorageRoot)
                ? OperationResult.Failure<string>(new StorageDefaultFolderHasHistoryException(State.DefaultStorageRoot))
                : Reachable(State.DefaultStorageRoot));

    // Either choice leaves the storage reachable at the folder it names, as the real one does.
    private OperationResult<string> Reachable(string root)
    {
        State.Startup = State.Startup with { Storage = new StorageStatus(true, root, null) };
        return OperationResult.Success(root);
    }
}
