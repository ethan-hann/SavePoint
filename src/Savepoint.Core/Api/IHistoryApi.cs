namespace Savepoint.Core.Api;

// "Go back to this" is a launch with LaunchTarget.Point, so it gets the same preview and dirty checks as Play.
public interface IHistoryApi
{
    Task<OperationResult<GameHistory>> GetAsync(string gameId, string? profileId = null, CancellationToken cancellationToken = default);
}
