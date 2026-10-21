namespace Savepoint.Core.Api;

/// <summary>One game's play session at one moment: from the launch request to the post-play capture.</summary>
/// <param name="GameId">The game being played.</param>
/// <param name="ProfileId">The profile the session plays.</param>
/// <param name="Target">What the launch was asked to load.</param>
/// <param name="StartedFromOlderPoint">True when the target was not the profile's latest point, so the post-play choices follow D14.</param>
/// <param name="LaunchJobId">The job that prepares, switches, and starts the game. Cancel the switch through <see cref="IJobsApi"/>.</param>
/// <param name="State">The current phase.</param>
/// <param name="Revision">Grows with every change, so a late subscriber can drop an event older than its snapshot.</param>
public sealed record LaunchSessionInfo(
    string GameId,
    string ProfileId,
    LaunchTarget Target,
    bool StartedFromOlderPoint,
    Guid LaunchJobId,
    SessionState State,
    long Revision);
