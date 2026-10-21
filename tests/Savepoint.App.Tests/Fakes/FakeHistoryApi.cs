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
        _backend.Call(_api, nameof(GetAsync), [gameId, profileId], () =>
        {
            _backend.State.Game(gameId);
            var points = _backend.State.PointsOf(gameId)
                .Where(p => profileId is null || p.ProfileId == profileId)
                .OrderByDescending(p => p.CapturedAt);
            return OperationResult.Success(new GameHistory(gameId, profileId, [.. points]));
        });
}
