using Savepoint.Core.Api;
using Savepoint.Core.Backend;

namespace Savepoint.Core.Services.AppStatus;

/// <summary>The real <see cref="IAppStatusApi"/>: the startup checks and catalog recovery.</summary>
internal sealed class AppStatusApi : IAppStatusApi
{
    private readonly BackendServices _services;

    public AppStatusApi(BackendServices services)
    {
        _services = services;
    }

    public Task<OperationResult<StartupStatus>> CheckAsync(CancellationToken cancellationToken = default) =>
        ApiGuard.NotBuilt<StartupStatus>();

    public Task<OperationResult<string>> RecoverCatalogFileAsync(string path, CatalogRecovery recovery, CancellationToken cancellationToken = default) =>
        ApiGuard.NotBuilt<string>();
}
