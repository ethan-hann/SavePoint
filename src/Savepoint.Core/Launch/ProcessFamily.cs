using Savepoint.Core.Paths;
using Savepoint.Core.Platform;

namespace Savepoint.Core.Launch;

/// <summary>
/// The processes started, directly or through others, by the program SavePoint started, as far as successive process lists show them.
/// Only descendants whose program lies in the game's folder are reported, and known crash reporters are left out.
/// </summary>
/// <remarks>
/// A parent id stays on a child after the parent is gone, and the OS hands the parent's id to a later process, so a parent id alone
/// proves nothing. A process only joins when it started no earlier than the member it names as parent, and not when a different
/// process now holds that id and the candidate started after that one did. Members are remembered after they exit so a grandchild
/// is still found when the process between it and the root has already gone. Without parent ids or start times in the list, nothing joins.
/// </remarks>
internal sealed class ProcessFamily
{
    // Crash reporters ship in the game's own folder and outlive the game, so the folder rule alone would follow them. Names are compared
    // without extension, ignoring case.
    private static readonly HashSet<string> _helpers = new(StringComparer.OrdinalIgnoreCase)
    {
        "crashreportclient", "unitycrashhandler64", "unitycrashhandler32", "crashpad_handler", "werfault",
    };

    // Start is null for a root that was never seen in a list.
    private readonly Dictionary<int, DateTimeOffset?> _members = [];
    private readonly HashSet<int> _roots = [];
    private readonly HashSet<int> _confirmed = [];
    private readonly DateTimeOffset _floor;
    private readonly PathSyntax _paths;
    private readonly string[] _folders;

    /// <summary>Creates an empty family.</summary>
    /// <param name="floor">The earliest start a child of a root with no known start can have: the session's start less a little tolerance.</param>
    /// <param name="folder">The game's install folder; only a descendant whose program lies inside it is reported. Null reports none.</param>
    /// <param name="paths">The path rules of the OS the process list came from.</param>
    public ProcessFamily(DateTimeOffset floor, string? folder, PathSyntax paths)
    {
        _floor = floor;
        _paths = paths;
        _folders = folder is null ? [] : [.. Forms(folder).Select(form => form.EndsWith(paths.Separator) ? form : form + paths.Separator)];
    }

    // The path as given and as the file system resolves it, so a link in either does not hide a match.
    private IEnumerable<string> Forms(string path)
    {
        var forms = new List<string>();
        try
        {
            forms.Add(_paths.Normalize(path));
        }
        catch (ArgumentException)
        {
            return forms;
        }

        try
        {
            var real = _paths.Normalize(RealPath.Resolve(path));
            if (!forms.Contains(real, StringComparer.Ordinal))
            {
                forms.Add(real);
            }
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // The path does not exist on this machine; the plain form stands.
        }

        return forms;
    }

    private bool InFolder(string? executable) =>
        !string.IsNullOrWhiteSpace(executable) && Forms(executable).Any(form => _folders.Any(folder => form.StartsWith(folder, _paths.Comparison)));

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
            if (_roots.Contains(pid) || !byPid.TryGetValue(pid, out var running))
            {
                continue;
            }

            // One look that cannot read a follower's start or program does not make it vanish; it was matched by both before.
            var sameStart = running.StartedAt is null || running.StartedAt == start;
            if (sameStart && InFolder(running.ExecutablePath))
            {
                _confirmed.Add(pid);
                alive.Add((pid, start));
            }
            else if (sameStart && running.ExecutablePath is null && _confirmed.Contains(pid))
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
