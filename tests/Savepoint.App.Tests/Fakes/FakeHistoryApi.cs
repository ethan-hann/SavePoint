using Savepoint.Core.Api;

namespace Savepoint.App.Tests.Fakes;

public sealed class FakeHistoryApi : IHistoryApi
{
    private const string _api = nameof(IHistoryApi);

    private readonly FakeBackend _backend;

    internal FakeHistoryApi(FakeBackend backend)
    {
        _backend = backend;
    }

    public Task<OperationResult<GameHistory>> GetAsync(string gameId, string? profileId = null, CancellationToken cancellationToken = default) =>
        _backend.Call(_api, nameof(GetAsync), [gameId, profileId], cancellationToken, () =>
        {
            _backend.State.Game(gameId);
            // Newest first; the clock stands still, so points saved at the same instant list the later one first, as real clocks would.
            var points = _backend.State.PointsOf(gameId)
                .Select((point, index) => (point, index))
                .Where(entry => profileId is null || entry.point.ProfileId == profileId)
                .OrderByDescending(entry => entry.point.CapturedAt)
                .ThenByDescending(entry => entry.index)
                .Select(entry => entry.point);
            return OperationResult.Success(new GameHistory(gameId, profileId, [.. points]));
        });

    public Task<OperationResult<RecentlyRemoved>> GetRecentlyRemovedAsync(string gameId, CancellationToken cancellationToken = default) =>
        _backend.Call(_api, nameof(GetRecentlyRemovedAsync), [gameId], cancellationToken, () =>
        {
            _backend.State.Game(gameId);
            var removed = _backend.State.Removed.Where(r => r.GameId == gameId).Select(r => r.Snapshot).OrderByDescending(snapshot => snapshot.RemovedAt);
            return OperationResult.Success(new RecentlyRemoved(gameId, [.. removed]));
        });
}
