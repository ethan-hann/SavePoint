using Savepoint.Core.Platform;

namespace Savepoint.Core.Launch;

/// <summary>
/// The processes started, directly or through others, by the program SavePoint started, as far as successive process lists show them.
/// Known helpers (stores, launchers, browsers, crash reporters) and what they start are left out.
/// </summary>
/// <remarks>
/// A parent id stays on a child after the parent is gone, and the OS hands the parent's id to a later process, so a parent id alone
/// proves nothing. A process only joins when it started no earlier than the member it names as parent, and not when a different
/// process now holds that id and the candidate started after that one did. Members are remembered after they exit so a grandchild
/// is still found when the process between it and the root has already gone. Without parent ids or start times in the list, nothing joins.
/// </remarks>
internal sealed class ProcessFamily
{
    // Programs that a game starts and that outlive it (stores, launchers, browsers, crash reporters). Following them would hold the
    // session open long after the game quit, so they and what they start never count. Names are compared without extension, ignoring case.
    private static readonly HashSet<string> _helpers = new(StringComparer.OrdinalIgnoreCase)
    {
        "steam", "steamwebhelper", "steamservice", "epicgameslauncher", "epicwebhelper", "galaxyclient", "galaxyclient helper",
        "gogalaxy", "upc", "uplaywebcore", "eadesktop", "eabackgroundservice", "origin", "rockstarservice", "launcher",
        "crashreportclient", "unitycrashhandler64", "unitycrashhandler32", "werfault", "chrome", "msedge", "firefox", "opera", "brave",
        "conhost",
    };

    // Start is null for a root that was never seen in a list.
    private readonly Dictionary<int, DateTimeOffset?> _members = [];
    private readonly HashSet<int> _roots = [];
    private readonly DateTimeOffset _floor;

    /// <summary>Creates an empty family.</summary>
    /// <param name="floor">The earliest start a child of a root with no known start can have: the session's start less a little tolerance.</param>
    public ProcessFamily(DateTimeOffset floor)
    {
        _floor = floor;
    }

    /// <summary>Reads one process list and returns the descendants of the started program that are running in it.</summary>
    /// <param name="listed">The whole process list.</param>
    /// <param name="startedPid">The id of the program SavePoint started, or null for a link (which has no family).</param>
    /// <param name="startedAlive">True while that program has not exited, so its id still names it.</param>
    public IReadOnlyList<(int Pid, DateTimeOffset? Start)> Update(IReadOnlyList<RunningProcess> listed, int? startedPid, bool startedAlive)
    {
        var byPid = new Dictionary<int, RunningProcess>(listed.Count);
        foreach (var process in listed)
        {
            byPid[process.Pid] = process;
        }

        if (startedPid is { } started && (startedAlive || !_members.ContainsKey(started)))
        {
            byPid.TryGetValue(started, out var self);
            AddRoot(started, startedAlive ? self?.StartedAt : null);
        }

        var grew = true;
        while (grew)
        {
            grew = false;
            foreach (var process in listed)
            {
                if (!_members.ContainsKey(process.Pid) && IsChildOfMember(process, byPid))
                {
                    _members[process.Pid] = process.StartedAt;
                    grew = true;
                }
            }
        }

        var alive = new List<(int, DateTimeOffset?)>();
        foreach (var (pid, start) in _members)
        {
            if (!_roots.Contains(pid) && byPid.TryGetValue(pid, out var running) && running.StartedAt == start)
            {
                alive.Add((pid, start));
            }
        }

        return alive;
    }

    private void AddRoot(int pid, DateTimeOffset? start)
    {
        _roots.Add(pid);
        if (!_members.TryGetValue(pid, out var known) || known is null || start is not null)
        {
            _members[pid] = start ?? known;
        }
    }

    private bool IsChildOfMember(RunningProcess process, Dictionary<int, RunningProcess> byPid)
    {
        if (process.ParentPid is not { } parent || process.StartedAt is not { } started || !_members.TryGetValue(parent, out var parentStart)
            || _helpers.Contains(NameWithoutExtension(process.Name)))
        {
            return false;
        }

        if (started < (parentStart ?? _floor))
        {
            return false;
        }

        // The id now belongs to another process that began before this one: this one is that process's child, not the member's.
        // A root never seen in a list has no start to compare, and a live holder of its id cannot be it, since a live one is always listed.
        return !(byPid.TryGetValue(parent, out var holder) && holder.StartedAt is { } heldSince && started >= heldSince
            && (parentStart is null || heldSince != parentStart));
    }

    private static string NameWithoutExtension(string name) =>
        name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
}
