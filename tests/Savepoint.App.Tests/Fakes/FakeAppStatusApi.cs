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

    public async Task<OperationResult<StartupStatus>> CheckAsync(CancellationToken cancellationToken = default, IProgress<StartupProgress>? progress = null)
    {
        foreach (var step in State.StartupSteps)
        {
            progress?.Report(step);
        }

        if (State.StartupGate is { } gate)
        {
            await gate;
        }

        return await _backend.Call(_api, nameof(CheckAsync), [], cancellationToken, () => OperationResult.Success(Checked()));
    }

    // As the real check: the storage is only looked at once git runs and every catalog file reads, and a scenario that names no storage
    // folder has the one the settings name, reachable.
    private StartupStatus Checked()
    {
        var startup = State.Startup;
        return !startup.Git.Available || startup.Catalog.CorruptFiles.Count > 0
            ? startup with { Storage = null }
            : startup with { Storage = startup.Storage ?? new StorageStatus(true, State.Settings.StorageRoot, null) };
    }

    // Both startup choices are for a folder that is gone; with the saved folder in reach there is nothing to find or start over from.
    private void RequireStorageLost()
    {
        if (Checked().Storage is { Reachable: true } reachable)
        {
            throw new StorageStillReachableException(reachable.Root);
        }
    }

    public Task<OperationResult<string>> RecoverCatalogFileAsync(string path, CatalogRecovery recovery) =>
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
            return OperationResult.Success($"{path}.corrupt-{_backend.Clock.GetUtcNow():yyyyMMdd'T'HHmmssfff'Z'}");
        });

    // As the real one: only a folder that holds the history of the games is taken, and nothing changes for any other.
    public Task<OperationResult<string>> UseStorageFolderAsync(string folder) =>
        _backend.Call(_api, nameof(UseStorageFolderAsync), [folder], () =>
        {
            RequireStorageLost();
            return State.StorageFoldersWithHistory.Contains(folder)
                ? Reachable(folder)
                : OperationResult.Failure<string>(new StorageHistoryNotFoundException(folder));
        });

    // As the real one: history already in the default folder is not an empty start.
    public Task<OperationResult<string>> StartWithEmptyStorageAsync() =>
        _backend.Call(_api, nameof(StartWithEmptyStorageAsync), [], () =>
        {
            RequireStorageLost();
            return State.StorageFoldersWithHistory.Contains(State.DefaultStorageRoot)
                ? OperationResult.Failure<string>(new StorageDefaultFolderHasHistoryException(State.DefaultStorageRoot))
                : Reachable(State.DefaultStorageRoot);
        });

    // Either choice leaves the storage reachable at the folder it names, and the settings name that folder from then on, as the real one does.
    private OperationResult<string> Reachable(string root)
    {
        State.Startup = State.Startup with { Storage = new StorageStatus(true, root, null) };
        State.Settings = State.Settings with { StorageRoot = root };
        return OperationResult.Success(root);
    }
}
