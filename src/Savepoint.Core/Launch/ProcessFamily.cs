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

    /// <summary>How much earlier than the session's start a process may still be one the session started, for clocks that differ a little.</summary>
    public static readonly TimeSpan SessionTolerance = TimeSpan.FromSeconds(10);

    // Start is null for a root that was never seen in a list.
    private readonly Dictionary<int, DateTimeOffset?> _members = [];
    private readonly HashSet<int> _roots = [];
    private readonly HashSet<int> _confirmed = [];
    private readonly DateTimeOffset _floor;
    private readonly PathSyntax _paths;
    private readonly string[] _folders;
    private readonly bool _linkOrphans;
    private readonly TimeSpan _window;
    private bool _orphanDecided;

    /// <summary>Creates an empty family.</summary>
    /// <param name="floor">The earliest start a child of a root with no known start can have: the session's start less a little tolerance.</param>
    /// <param name="folder">The game's install folder; only a descendant whose program lies inside it is reported. Null reports none.</param>
    /// <param name="paths">The path rules of the OS the process list came from.</param>
    /// <param name="linkOrphans">
    /// True where a process whose parent exits is handed to another parent (Linux): a starter that is gone then has its game linked
    /// by when the game started and where its program lies, since the game no longer names the starter.
    /// </param>
    /// <param name="window">How long after the starter started a process may start and still be taken for what the starter handed over to.</param>
    public ProcessFamily(DateTimeOffset floor, string? folder, PathSyntax paths, bool linkOrphans = false, TimeSpan window = default)
    {
        _floor = floor;
        _paths = paths;
        _linkOrphans = linkOrphans;
        _window = window;
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

        if (_linkOrphans && !startedAlive && startedPid is { } gone)
        {
            LinkOrphan(listed, byPid, gone);
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

    /// <summary>
    /// The ids of the processes that each looked like what a gone starter handed over to when more than one did, so none was followed;
    /// empty when the match was clear or nothing matched.
    /// </summary>
    public IReadOnlyList<int> AmbiguousHandover { get; private set; } = [];

    // The starter is gone, so no parent id names it. Exactly one process in the game's folder that started inside the window after the
    // starter did, and was not started by a process of this session, is taken for the game; two or more could be a second copy or a
    // server the player started, so none is. The first answer stands for the rest of the session.
    private void LinkOrphan(IReadOnlyList<RunningProcess> listed, Dictionary<int, RunningProcess> byPid, int root)
    {
        if (_orphanDecided)
        {
            return;
        }

        var begun = _members.TryGetValue(root, out var rootStart) ? rootStart : null;
        var from = begun ?? _floor;
        var until = (begun ?? _floor + SessionTolerance) + _window;
        var candidates = listed.Where(process => process.Pid != root && !_members.ContainsKey(process.Pid)
            && process.StartedAt is { } started && started >= from && started <= until
            && !_helpers.Contains(NameWithoutExtension(process.Name)) && InFolder(process.ExecutablePath)
            && !(process.ParentPid is { } parent && byPid.TryGetValue(parent, out var holder) && holder.StartedAt is { } parentStart && parentStart >= from))
            .ToList();
        if (candidates.Count == 1)
        {
            _members[candidates[0].Pid] = candidates[0].StartedAt;
            _orphanDecided = true;
        }
        else if (candidates.Count > 1)
        {
            AmbiguousHandover = [.. candidates.Select(process => process.Pid).Order()];
            _orphanDecided = true;
        }
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
