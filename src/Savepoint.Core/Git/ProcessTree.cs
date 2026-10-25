using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;

namespace Savepoint.Core.Git;

// Ends a process and everything it started. .NET's own tree kill stops each process first (SIGSTOP) and ends it a moment later;
// a host that exits in between leaves the process stopped for good, still holding its files and, for an AppImage, its mount.
// On Linux the tree is read from /proc and every member gets SIGKILL straight away, which also ends a stopped one.
internal static class ProcessTree
{
    private const int _sigkill = 9;

    // The root stays alive until the last pass, so a pass can pick up a new direct child of it that an earlier pass missed.
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
        try
        {
            // Deepest first and the root last: a parent that is still alive can be found again by the next pass if it forked
            // while the passes ran, whereas a child of a dead parent is handed to init and cannot be traced back.
            for (var pass = 0; pass < _linuxPasses; pass++)
            {
                var below = DescendantsOf(process.Id, procRoot);
                if (below.Count == 0)
                {
                    break;
                }

                for (var i = below.Count - 1; i >= 0; i--)
                {
                    SignalIfStillChildOf(below[i].Id, below[i].Parent, procRoot);
                }
            }
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            return false;
        }

        try
        {
            // Without a stop signal on Linux; .NET also knows whether it already reaped the process.
            process.Kill();
        }
        catch (Exception e) when (e is InvalidOperationException or Win32Exception)
        {
            // Already gone.
        }

        return true;
    }

    // A number can be handed to another process between the scan and the signal, so the parent is read again first.
    private static void SignalIfStillChildOf(int id, int parent, string procRoot)
    {
        try
        {
            var stat = File.ReadAllText(Path.Combine(procRoot, id.ToString(CultureInfo.InvariantCulture), "stat"));
            if (StateAndParent(stat) is { } fields && fields.State is not ('Z' or 'X' or 'x') && fields.Parent == parent)
            {
                _ = kill(id, _sigkill);
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
