namespace Savepoint.Core.Api;

/// <summary>
/// Reads a game's history. "Go back to this" is a launch with <see cref="LaunchTarget.Point"/>, so it gets the same preview and
/// dirty checks as Play.
/// </summary>
/// <remarks>See <see cref="OperationResult{T}"/> for the rules every member follows.</remarks>
public interface IHistoryApi
{
    /// <summary>Lists a game's history points, newest first.</summary>
    /// <param name="gameId">The game.</param>
    /// <param name="profileId">Only this profile's points; null for every profile's.</param>
    /// <param name="cancellationToken">Cancelling fails the call with <c>error.operation.cancelled</c> (D23).</param>
    Task<OperationResult<GameHistory>> GetAsync(string gameId, string? profileId = null, CancellationToken cancellationToken = default);
}
