using Savepoint.Core.Api;

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
}
