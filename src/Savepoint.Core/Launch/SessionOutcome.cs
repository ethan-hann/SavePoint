namespace Savepoint.Core.Launch;

/// <summary>What a finished play session came to.</summary>
/// <param name="Kind">How the end was detected.</param>
/// <param name="StartedAt">When the session began: when the game was started or its link handed over.</param>
/// <param name="EndedAt">When SavePoint settled the session, on the clock the watcher was given; not when a process the watcher could not see stopped.</param>
/// <param name="ChildExitCode">The exit code of the program SavePoint started, or null for a link and for a program still running when the session settled.</param>
public sealed record SessionOutcome(SessionEndKind Kind, DateTimeOffset StartedAt, DateTimeOffset EndedAt, int? ChildExitCode)
{
    /// <summary>The process id of the program SavePoint started, or null for a link.</summary>
    public int? StartedPid { get; init; }

    /// <summary>
    /// Whether any process listed during the session had the configured process name, the started program included; null when no process
    /// name is configured. False at the end of a session means the name never matched anything, so a game that went unnoticed is told
    /// apart from a name that is wrong.
    /// </summary>
    public bool? NamedProcessSeen { get; init; }

    /// <summary>The ids of the processes with the configured name, other than the started program, that the watcher saw; empty when it saw none.</summary>
    public IReadOnlyList<int> FollowedPids { get; init; } = [];

    /// <summary>
    /// The ids of the processes in the game's folder that could each have been what a starter that exited before the first look handed
    /// over to, when there was more than one and so none was followed; empty otherwise.
    /// </summary>
    public IReadOnlyList<int> AmbiguousHandoverPids { get; init; } = [];

    /// <summary>The time from the start to the end; for <see cref="SessionEndKind.NamedProcessNeverAppeared"/> it includes the grace period that found nothing.</summary>
    public TimeSpan PlayDuration => EndedAt - StartedAt;
}
