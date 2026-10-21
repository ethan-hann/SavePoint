namespace Savepoint.Core.Api;

public interface IAppStatusApi
{
    Task<OperationResult<StartupStatus>> CheckAsync(CancellationToken cancellationToken = default);

    // Answers with where the corrupt file was moved, so the recovery screen can say so; check again afterwards.
    Task<OperationResult<string>> RecoverCatalogFileAsync(string path, CatalogRecovery recovery, CancellationToken cancellationToken = default);
}
