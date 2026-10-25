using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

namespace Savepoint.Core.Git;

// Ends a process and everything it started. .NET's own tree kill stops each process first (SIGSTOP) and ends it a moment later,
// and nothing finishes it if the host exits in between. On Linux the tree is read from /proc, frozen, and then every member gets
// SIGKILL in the same call (the exit-time registry runs it before the host goes), so nothing is left stopped.
internal static class ProcessTree
{
    private const int _sigkill = 9;
    private const int _sigstop = 19;

    // Enough for a tree a few levels deep to be frozen level by level.
    private const int _linuxPasses = 5;

    public static void Kill(Process process)
    {
        if (OperatingSystem.IsLinux() && TryKillLinuxTree(process, "/proc"))
        {
            return;
        }

        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (Exception e) when (e is InvalidOperationException or Win32Exception or AggregateException)
        {
            // Already gone, or part of the tree could not be reached; take at least the process itself.
            try
            {
                process.Kill();
            }
            catch (Exception inner) when (inner is InvalidOperationException or Win32Exception)
            {
                // Nothing more can be done from here.
            }
        }
    }

    /// <summary>Every live descendant of a process, found from the parent ids in a /proc folder; the process itself is not in the list.</summary>
    internal static List<(int Id, int Parent)> DescendantsOf(int rootId, string procRoot)
    {
        var children = new Dictionary<int, List<int>>();
        foreach (var (id, parent) in ReadParents(procRoot))
        {
            if (!children.TryGetValue(parent, out var list))
            {
                children[parent] = list = [];
            }

            list.Add(id);
        }

        var found = new List<(int Id, int Parent)>();
        var queue = new Queue<int>();
        queue.Enqueue(rootId);
        while (queue.Count > 0)
        {
            var parent = queue.Dequeue();
            if (!children.TryGetValue(parent, out var below))
            {
                continue;
            }

            foreach (var child in below)
            {
                found.Add((child, parent));
                queue.Enqueue(child);
            }
        }

        return found;
    }

    // False when the system call cannot be bound, so the caller falls back to the .NET kill instead of failing the cancel.
    internal static bool TryKillLinuxTree(Process process, string procRoot)
    {
        var known = new List<(int Id, int Parent)>();
        try
        {
            // A process that forks after it was read, and then dies first, hands its child to init where it can no longer be
            // traced. So each process found is stopped (briefly: the kill follows at once), which freezes the tree while it is
            // read again until a pass finds nothing new; only then does everything get SIGKILL, deepest first.
            if (!process.HasExited)
            {
                _ = kill(process.Id, _sigstop);
            }

            for (var pass = 0; pass < _linuxPasses; pass++)
            {
                var fresh = DescendantsOf(process.Id, procRoot).Where(item => !known.Contains(item)).ToList();
                if (fresh.Count == 0)
                {
                    break;
                }

                foreach (var (id, parent) in fresh)
                {
                    SignalIfStillChildOf(id, parent, _sigstop, procRoot);
                    known.Add((id, parent));
                }
            }
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }
        finally
        {
            for (var i = known.Count - 1; i >= 0; i--)
            {
                SignalIfStillChildOf(known[i].Id, known[i].Parent, _sigkill, procRoot);
            }
        }

        try
        {
            // .NET knows whether it already reaped the process.
            process.Kill();
        }
        catch (Exception e) when (e is InvalidOperationException or Win32Exception)
        {
            // Already gone.
        }

        return true;
    }

    // A number can be handed to another process between the scan and the signal, so the parent is read again first.
    private static void SignalIfStillChildOf(int id, int parent, int signal, string procRoot)
    {
        try
        {
            var stat = File.ReadAllText(Path.Combine(procRoot, id.ToString(CultureInfo.InvariantCulture), "stat"));
            if (StateAndParent(stat) is { } fields && fields.State is not ('Z' or 'X' or 'x') && fields.Parent == parent)
            {
                _ = kill(id, signal);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Gone already.
        }
    }

    private static IEnumerable<(int Id, int Parent)> ReadParents(string procRoot)
    {
        IEnumerable<string> folders;
        try
        {
            folders = Directory.EnumerateDirectories(procRoot).ToList();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            yield break;
        }

        foreach (var folder in folders)
        {
            if (!int.TryParse(Path.GetFileName(folder), NumberStyles.None, CultureInfo.InvariantCulture, out var id))
            {
                continue;
            }

            string stat;
            try
            {
                stat = File.ReadAllText(Path.Combine(folder, "stat"));
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            if (StateAndParent(stat) is { } fields && fields.State is not ('Z' or 'X' or 'x'))
            {
                yield return (id, fields.Parent);
            }
        }
    }

    // The name is in brackets and may hold brackets and spaces itself, so the fields are counted from the last closing one.
    private static (char State, int Parent)? StateAndParent(string stat)
    {
        var close = stat.LastIndexOf(')');
        if (close < 0)
        {
            return null;
        }

        var rest = stat[(close + 1)..].Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return rest.Length >= 2 && int.TryParse(rest[1], NumberStyles.None, CultureInfo.InvariantCulture, out var parent)
            ? (rest[0][0], parent)
            : null;
    }

    [DllImport("libc")]
    private static extern int kill(int pid, int signal);
}
