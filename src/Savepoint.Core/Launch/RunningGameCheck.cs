using Savepoint.Core.Platform;

namespace Savepoint.Core.Launch;

/// <summary>Asks the OS whether a game is running, by the process name its definition gives.</summary>
/// <remarks>
/// This is the pre-launch question. <see cref="GameSessionWatcher.IsRunning"/> answers it for a game SavePoint is already tracking,
/// where a program it started counts too.
/// </remarks>
public sealed class RunningGameCheck
{
    private readonly IProcessInspector _processes;
    private readonly OsKind _os;

    /// <summary>Creates a check over a process list.</summary>
    /// <param name="processes">Where running processes come from.</param>
    /// <param name="os">The OS the list came from; it decides how names are compared (<see cref="ProcessNameMatcher"/>).</param>
    public RunningGameCheck(IProcessInspector processes, OsKind os)
    {
        _processes = processes;
        _os = os;
    }

    /// <summary>True when a running process matches <paramref name="processName"/>.</summary>
    /// <param name="processName">The process name from the game definition; a blank name matches nothing.</param>
    /// <remarks>An unreadable process list counts as not running: the check cannot refuse a launch on a guess.</remarks>
    public bool IsRunning(string processName) => Probe(processName) == NamedProcessState.Running;

    // The inspectors report a list they could not read as an empty one, and a machine always runs something.
    internal NamedProcessState Probe(string processName, int? ignoringPid = null)
    {
        if (string.IsNullOrWhiteSpace(processName))
        {
            return NamedProcessState.Absent;
        }

        IReadOnlyList<RunningProcess> running;
        try
        {
            running = _processes.List();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The contract says List never throws; a list that cannot be read says nothing either way.
            return NamedProcessState.Unknown;
        }

        if (running.Count == 0)
        {
            return NamedProcessState.Unknown;
        }

        return running.Any(process => process.Pid != ignoringPid && ProcessNameMatcher.Matches(processName, process, _os))
            ? NamedProcessState.Running
            : NamedProcessState.Absent;
    }
}

/// <summary>What one read of the process list says about a named process.</summary>
internal enum NamedProcessState
{
    /// <summary>The list could not be read, so nothing can be said.</summary>
    Unknown,

    /// <summary>A matching process is running.</summary>
    Running,

    /// <summary>The list was read and holds no matching process.</summary>
    Absent,
}
