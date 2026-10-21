using Savepoint.Core.Api;
using Savepoint.Core.Backend;
using Savepoint.Core.Errors;

namespace Savepoint.Core.Services.History;

/// <summary>The real <see cref="IHistoryApi"/>: capture events read back from the git directories alone.</summary>
internal sealed class HistoryApi : IHistoryApi
{
    private readonly BackendServices _services;
    private readonly HistoryReader _reader;

    public HistoryApi(BackendServices services)
    {
        _services = services;
        _reader = new HistoryReader(services);
    }

    public Task<OperationResult<GameHistory>> GetAsync(string gameId, string? profileId = null, CancellationToken cancellationToken = default) =>
        ApiGuard.RunAsync(
            async () =>
            {
                var game = await _services.Catalog.GetGameAsync(gameId, cancellationToken).ConfigureAwait(false)
                    ?? throw new GameNotFoundException(gameId);
                var profiles = await _services.Catalog.ListProfilesAsync(gameId, cancellationToken).ConfigureAwait(false);
                return await _services.Storage.ReadAsync(
                    storage => _reader.ReadAsync(storage, game, profiles, profileId, cancellationToken),
                    cancellationToken).ConfigureAwait(false);
            },
            cancellationToken);
}
