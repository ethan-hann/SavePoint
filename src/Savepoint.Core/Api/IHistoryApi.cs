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

    /// <summary>
    /// Lists the snapshots that left a profile, the most recently removed first: the newer snapshots "Continue the profile from this
    /// session" takes off it, and what "Replace the snapshot with this session" rewrites. Each stays here, kept by a record that is
    /// outside every profile, until the player next frees up space; nothing else deletes it. A removed snapshot is part of no profile, so
    /// <see cref="GetAsync"/>, the delete-profile warning, a profile's snapshot count and every launch leave it out. Reads only the git
    /// directories, so it works while a game folder has moved.
    /// </summary>
    /// <param name="gameId">The game.</param>
    /// <param name="cancellationToken">Cancelling fails the call with <c>error.operation.cancelled</c> (D23).</param>
    Task<OperationResult<RecentlyRemoved>> GetRecentlyRemovedAsync(string gameId, CancellationToken cancellationToken = default);
}
