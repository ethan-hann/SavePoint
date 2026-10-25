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

    // Each pass can only miss a process forked after it looked, so a few passes settle a tree that is being torn down.
    private const int _linuxPasses = 5;

    public static void Kill(Process process)
    {
        if (OperatingSystem.IsLinux())
        {
            KillLinuxTree(process.Id, "/proc");
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
    internal static List<int> DescendantsOf(int rootId, string procRoot)
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

        var found = new List<int>();
        var queue = new Queue<int>();
        queue.Enqueue(rootId);
        while (queue.Count > 0)
        {
            if (!children.TryGetValue(queue.Dequeue(), out var below))
            {
                continue;
            }

            foreach (var child in below)
            {
                found.Add(child);
                queue.Enqueue(child);
            }
        }

        return found;
    }

    internal static void KillLinuxTree(int rootId, string procRoot)
    {
        // Deepest first and the root last: a parent that is still alive can be found again by the next pass if it forked
        // while the passes ran, whereas a child of a dead parent is handed to init and cannot be traced back.
        for (var pass = 0; pass < _linuxPasses; pass++)
        {
            var below = DescendantsOf(rootId, procRoot);
            if (below.Count == 0)
            {
                break;
            }

            for (var i = below.Count - 1; i >= 0; i--)
            {
                Signal(below[i], procRoot);
            }
        }

        Signal(rootId, procRoot);
    }

    // True when the process existed and was sent the signal; a process that is already a zombie is not worth one.
    private static bool Signal(int id, string procRoot)
    {
        if (LiveState(id, procRoot) is null)
        {
            return false;
        }

        return kill(id, _sigkill) == 0;
    }

    private static char? LiveState(int id, string procRoot)
    {
        try
        {
            var stat = File.ReadAllText(Path.Combine(procRoot, id.ToString(CultureInfo.InvariantCulture), "stat"));
            var state = StateAndParent(stat)?.State;
            return state is 'Z' or 'X' or 'x' ? null : state;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
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

    [DllImport("libc", SetLastError = true)]
    private static extern int kill(int pid, int signal);
}
