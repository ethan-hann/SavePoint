using Savepoint.Core.Api;
using Savepoint.Core.Backend;

namespace Savepoint.Core.Services.History;

/// <summary>The real <see cref="IHistoryApi"/>: capture events read back from the git directories alone.</summary>
internal sealed class HistoryApi : IHistoryApi
{
    private readonly BackendServices _services;

    public HistoryApi(BackendServices services)
    {
        _services = services;
    }

    public Task<OperationResult<GameHistory>> GetAsync(string gameId, string? profileId = null, CancellationToken cancellationToken = default) =>
        ApiGuard.NotBuilt<GameHistory>();
}
