using System.Collections.Concurrent;
using System.ComponentModel;
using System.Diagnostics;
using Savepoint.Core.Diagnostics;

namespace Savepoint.Core.Git;

// Every git a runner has started and not yet seen end. When the app exits while a call is still being cancelled, or one is
// running that nobody cancelled, nothing else would end it: a git left behind keeps files open and, from an AppImage, the mount.
//
// Given a note path it also keeps the note of running git (see RunningGitNote) so a SavePoint that is stopped from outside, which runs no
// exit handler, still leaves the next start a list of what to end. The note is written from a timer a short delay after the set of gits
// changed, and only when it differs from what was written last, so the many calls that end within the delay (status, rev-parse, ...) cost
// nothing and a snapshot or switch that runs for minutes is on disk half a second after it starts. The price: a SavePoint stopped from
// outside within that delay of starting a git leaves no note of it. A git that short is over by the time the next start looks.
internal sealed class GitProcessRegistry
{
    private static readonly TimeSpan _defaultNoteDelay = TimeSpan.FromMilliseconds(500);
    // Static because a runner built without its own registry must still have its gits ended at exit, and only one exit hook may reach them (D59).
    private static readonly Lazy<GitProcessRegistry> _shared = new(CreateShared);

    // Static because the exit handler is process-wide and must reach the registry of every backend still alive when the process ends (D59).
    private static readonly ConcurrentQueue<GitProcessRegistry> _forApp = new();

    // Static because the process has one exit event; hooking it once per registry would close each registry once per hook.
    private static int _exitHooked;

    private readonly ConcurrentDictionary<int, Entry> _running = new();
    private readonly string? _notePath;
    private readonly TimeSpan _noteDelay;
    private readonly IDiagnosticLog? _log;
    private readonly Lock _noteGate = new();
    private readonly List<ProcessStamp> _carried = [];
    private HashSet<ProcessStamp> _written = [];
    private Timer? _timer;
    private bool _closed;
    private bool _warned;

    /// <param name="notePath">Where the note of running git is kept; none is kept when null.</param>
    /// <param name="noteDelay">How long after a change the note is written; half a second when null.</param>
    /// <param name="log">Where a note that cannot be written is mentioned, once.</param>
    public GitProcessRegistry(string? notePath = null, TimeSpan? noteDelay = null, IDiagnosticLog? log = null)
    {
        _notePath = notePath;
        _noteDelay = noteDelay ?? _defaultNoteDelay;
        _log = log;
    }

    /// <summary>The registry every runner uses unless it is given its own; it is emptied when the process exits.</summary>
    public static GitProcessRegistry Shared => _shared.Value;

    /// <summary>A registry that keeps the note of running git, and ends its gits and removes the note when the process exits.</summary>
    /// <param name="notePath">Where the note is kept.</param>
    /// <param name="noteDelay">How long after a change the note is written; half a second when null.</param>
    /// <param name="log">Where a note that cannot be written is mentioned.</param>
    public static GitProcessRegistry ForApp(string notePath, TimeSpan? noteDelay, IDiagnosticLog log)
    {
        var registry = new GitProcessRegistry(notePath, noteDelay, log);
        _forApp.Enqueue(registry);
        if (Interlocked.Exchange(ref _exitHooked, 1) == 0)
        {
            AppDomain.CurrentDomain.ProcessExit += (_, _) =>
            {
                foreach (var each in _forApp)
                {
                    each.Close();
                }
            };
        }

        return registry;
    }

    private static GitProcessRegistry CreateShared()
    {
        var registry = new GitProcessRegistry();
        AppDomain.CurrentDomain.ProcessExit += (_, _) => registry.KillAll();
        return registry;
    }

    /// <summary>Notes a started process; the returned token takes it off the list again once its call is over.</summary>
    /// <param name="process">The process just started.</param>
    /// <param name="persist">False for a git that is left to finish when SavePoint is stopped, which the note of running git must not name.</param>
    public IDisposable Add(Process process, bool persist = true) =>
        ProcessStamp.Of(process) is { } stamp ? Add(stamp, persist) : Registration.None;

    /// <summary>Notes a process by its stamp.</summary>
    /// <param name="stamp">The process.</param>
    /// <param name="persist">False for a git that is left to finish when SavePoint is stopped, which the note of running git must not name.</param>
    public IDisposable Add(ProcessStamp stamp, bool persist = true)
    {
        _running[stamp.Id] = new Entry(stamp, persist);
        Touch();
        return new Registration(this, stamp);
    }

    /// <summary>Whether a process with this id is on the list; one that has ended may still be listed until its call returns.</summary>
    public bool Contains(int id) => _running.ContainsKey(id);

    /// <summary>
    /// Keeps gits an earlier run left in the note while they run, so a start that is stopped before it has ended them does not lose track of them.
    /// Ones that have ended drop out the next time the note is written, which this asks for.
    /// </summary>
    /// <param name="stamps">The gits to keep.</param>
    public void Carry(IEnumerable<ProcessStamp> stamps)
    {
        lock (_noteGate)
        {
            _carried.AddRange(stamps);
        }

        Touch();
    }

    /// <summary>Ends every registered process, removes the note and writes no more of it. Done as the process exits.</summary>
    public void Close()
    {
        lock (_noteGate)
        {
            _closed = true;
            _timer?.Dispose();
            _timer = null;
        }

        KillAll();
        if (_notePath is not null)
        {
            RunningGitNote.Clear(_notePath);
        }
    }

    /// <summary>Ends every registered process and what it started. A process that took over the number of one that ended is left alone.</summary>
    public void KillAll()
    {
        foreach (var (id, entry) in _running.ToArray())
        {
            var stamp = entry.Stamp;
            if (!stamp.IsRunning())
            {
                _running.TryRemove(new KeyValuePair<int, Entry>(id, entry));
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

    private void Touch()
    {
        if (_notePath is null)
        {
            return;
        }

        lock (_noteGate)
        {
            if (!_closed && _timer is null)
            {
                _timer = new Timer(_ => WriteNote(), null, _noteDelay, Timeout.InfiniteTimeSpan);
            }
        }
    }

    private void WriteNote()
    {
        lock (_noteGate)
        {
            _timer?.Dispose();
            _timer = null;
            if (_closed || _notePath is null)
            {
                return;
            }

            var current = _running.Values.Where(entry => entry.Persist).Select(entry => entry.Stamp).ToHashSet();
            foreach (var carried in _carried.ToList())
            {
                if (carried.IsRunning())
                {
                    current.Add(carried);
                }
                else
                {
                    _carried.Remove(carried);
                }
            }

            if (current.SetEquals(_written))
            {
                return;
            }

            try
            {
                if (current.Count == 0)
                {
                    RunningGitNote.Clear(_notePath);
                }
                else
                {
                    RunningGitNote.Write(_notePath, ProcessStamp.Current(), current);
                }

                _written = current;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // Tried again at the next change; a start after a stop from outside then has less to go on, which is all it costs.
                if (!_warned)
                {
                    _warned = true;
                    _log?.Warning($"The note of running git at {_notePath} could not be written.", e);
                }
            }
        }
    }

    private readonly record struct Entry(ProcessStamp Stamp, bool Persist);

    private sealed class Registration(GitProcessRegistry? owner, ProcessStamp stamp) : IDisposable
    {
        public static readonly Registration None = new(null, default);

        // Only this entry: a later process that was given the same number must stay listed.
        public void Dispose()
        {
            if (owner is not null && owner._running.TryGetValue(stamp.Id, out var entry) && entry.Stamp == stamp)
            {
                owner._running.TryRemove(new KeyValuePair<int, Entry>(stamp.Id, entry));
                owner.Touch();
            }
        }
    }
}
