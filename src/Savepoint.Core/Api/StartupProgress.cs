namespace Savepoint.Core.Api;

/// <summary>Something the startup check is waiting on, reported while <see cref="IAppStatusApi.CheckAsync"/> is still running.</summary>
public enum StartupProgress
{
    /// <summary>
    /// A cleanup of the saved history that an earlier run of SavePoint began is still going on after that run ended, and the check
    /// waits for it to finish before it touches the games. Reported once per stretch of waiting, before the wait begins.
    /// </summary>
    FinishingEarlierCleanup,
}
