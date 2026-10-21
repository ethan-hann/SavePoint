namespace Savepoint.Core.Api;

public sealed record LaunchSessionInfo(
    string GameId,
    string ProfileId,
    LaunchTarget Target,
    bool StartedFromOlderPoint,

    // The job that prepares, switches, and starts the game; cancel the switch through IJobsApi.
    Guid LaunchJobId,
    SessionState State,

    // Grows with every change, so a late subscriber can drop an event older than its snapshot.
    long Revision);
