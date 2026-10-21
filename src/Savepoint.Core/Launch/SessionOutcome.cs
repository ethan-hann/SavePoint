namespace Savepoint.Core.Launch;

/// <summary>What a finished play session came to.</summary>
/// <param name="Kind">How the end was detected.</param>
/// <param name="StartedAt">When the session began: when the game was started or its link handed over.</param>
/// <param name="EndedAt">When SavePoint settled the session, on the clock the watcher was given; not when a process the watcher could not see stopped.</param>
/// <param name="ChildExitCode">The exit code of the program SavePoint started, or null for a link and for a program still running when the session settled.</param>
public sealed record SessionOutcome(SessionEndKind Kind, DateTimeOffset StartedAt, DateTimeOffset EndedAt, int? ChildExitCode)
{
    /// <summary>The time from the start to the end; for <see cref="SessionEndKind.NamedProcessNeverAppeared"/> it includes the grace period that found nothing.</summary>
    public TimeSpan PlayDuration => EndedAt - StartedAt;
}
