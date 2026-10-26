using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;

namespace Savepoint.Core.Git;

// Every git a runner has started and not yet seen end. When the app exits while a call is still being cancelled, or one is
// running that nobody cancelled, nothing else would end it: a git left behind keeps files open and, from an AppImage, the mount.
internal sealed class GitProcessRegistry
{
    private static readonly Lazy<GitProcessRegistry> _shared = new(CreateShared);

    private readonly ConcurrentDictionary<int, ProcessStamp> _running = new();

    /// <summary>The registry every runner uses unless it is given its own; it is emptied when the process exits.</summary>
    public static GitProcessRegistry Shared => _shared.Value;

    private static GitProcessRegistry CreateShared()
    {
        var registry = new GitProcessRegistry();
        AppDomain.CurrentDomain.ProcessExit += (_, _) => registry.KillAll();
        return registry;
    }

    /// <summary>Notes a started process; the returned token takes it off the list again once its call is over.</summary>
    public IDisposable Add(Process process) =>
        ProcessStamp.Of(process) is { } stamp ? Add(stamp) : Registration.None;

    /// <summary>Notes a process by its stamp.</summary>
    public IDisposable Add(ProcessStamp stamp)
    {
        _running[stamp.Id] = stamp;
        return new Registration(this, stamp);
    }

    /// <summary>Whether a process with this id is on the list; one that has ended may still be listed until its call returns.</summary>
    public bool Contains(int id) => _running.ContainsKey(id);

    /// <summary>Ends every registered process and what it started. A process that took over the number of one that ended is left alone.</summary>
    public void KillAll()
    {
        foreach (var (id, stamp) in _running.ToArray())
        {
            if (!stamp.IsRunning())
            {
                _running.TryRemove(new KeyValuePair<int, ProcessStamp>(id, stamp));
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

    private sealed class Registration(GitProcessRegistry? owner, ProcessStamp stamp) : IDisposable
    {
        public static readonly Registration None = new(null, default);

        // Only this entry: a later process that was given the same number must stay listed.
        public void Dispose() => owner?._running.TryRemove(new KeyValuePair<int, ProcessStamp>(stamp.Id, stamp));
    }
}
