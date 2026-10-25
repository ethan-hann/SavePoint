using Savepoint.Core.Platform;

namespace Savepoint.Core.Launch;

/// <summary>
/// The processes started, directly or through others, by the program SavePoint started or by the game it follows by name, as far as
/// successive process lists show them.
/// </summary>
/// <remarks>
/// A parent id stays on a child after the parent is gone, and the OS hands the parent's id to a later process, so a parent id alone
/// proves nothing. A process only joins when it started no earlier than the member it names as parent, and not when a different
/// process now holds that id and the candidate started after that one did. Members are remembered after they exit so a grandchild
/// is still found when the process between it and the root has already gone. Without parent ids or start times in the list, nothing joins.
/// </remarks>
internal sealed class ProcessFamily
{
    // Start is null for a root that was never seen in a list.
    private readonly Dictionary<int, DateTimeOffset?> _members = [];
    private readonly HashSet<int> _roots = [];

    /// <summary>Reads one process list and returns the ids of the descendants of the roots that are running in it.</summary>
    /// <param name="listed">The whole process list.</param>
    /// <param name="startedPid">The id of the program SavePoint started, or null for a link.</param>
    /// <param name="startedAlive">True while that program has not exited, so its id still names it.</param>
    /// <param name="namedRoots">The running processes that match the configured name; what they start is followed too.</param>
    public IReadOnlyList<int> Update(IReadOnlyList<RunningProcess> listed, int? startedPid, bool startedAlive, IEnumerable<RunningProcess> namedRoots)
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

        foreach (var root in namedRoots)
        {
            AddRoot(root.Pid, root.StartedAt);
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

        var alive = new List<int>();
        foreach (var (pid, start) in _members)
        {
            if (!_roots.Contains(pid) && byPid.TryGetValue(pid, out var running) && running.StartedAt == start)
            {
                alive.Add(pid);
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
        if (process.ParentPid is not { } parent || process.StartedAt is not { } started || !_members.TryGetValue(parent, out var parentStart))
        {
            return false;
        }

        if (parentStart is { } earliest && started < earliest)
        {
            return false;
        }

        // The id now belongs to another process that began before this one: this one is that process's child, not the member's.
        return !(byPid.TryGetValue(parent, out var holder) && holder.StartedAt is { } heldSince && started >= heldSince
            && parentStart is { } known && heldSince != known);
    }
}
