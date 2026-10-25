using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;

namespace Savepoint.Core.Git;

// Every git the runners have started and not yet seen end. When the app exits while a call is still being cancelled, or one is
// running that nobody cancelled, nothing else would end it: a git left behind keeps files open and, from an AppImage, the mount.
internal static class GitProcessRegistry
{
    private static readonly ConcurrentDictionary<int, ProcessStamp> _running = new();

    static GitProcessRegistry()
    {
        AppDomain.CurrentDomain.ProcessExit += (_, _) => KillAll();
    }

    /// <summary>Notes a started process; the returned token takes it off the list again once its call is over.</summary>
    public static IDisposable Add(Process process)
    {
        var stamp = ProcessStamp.Of(process);
        if (stamp is null)
        {
            return Registration.None;
        }

        _running[stamp.Value.Id] = stamp.Value;
        return new Registration(stamp.Value.Id);
    }

    /// <summary>Ends every registered process and what it started. A process that took over the number of one that ended is left alone.</summary>
    public static void KillAll()
    {
        foreach (var (id, stamp) in _running.ToArray())
        {
            if (!stamp.IsRunning())
            {
                _running.TryRemove(id, out _);
                continue;
            }

            try
            {
                using var process = Process.GetProcessById(id);
                ProcessTree.Kill(process);
            }
            catch (Exception e) when (e is ArgumentException or InvalidOperationException or Win32Exception)
            {
                // Ended on its own in the meantime.
            }
        }
    }

    private sealed class Registration(int id) : IDisposable
    {
        public static readonly Registration None = new(0);

        public void Dispose()
        {
            if (id != 0)
            {
                _running.TryRemove(id, out _);
            }
        }
    }
}
