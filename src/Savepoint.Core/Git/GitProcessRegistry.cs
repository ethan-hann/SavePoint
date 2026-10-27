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
    private static readonly ConcurrentDictionary<GitProcessRegistry, byte> _forApp = new();

    // Static because the process has one exit event; hooking it once per registry would close each registry once per hook.
    private static int _exitHooked;

    private readonly ConcurrentDictionary<int, Entry> _running = new();
    private readonly string? _notePath;
    private readonly TimeSpan _noteDelay;
    private readonly IDiagnosticLog? _log;
    private readonly Lock _noteGate = new();
    private readonly List<ProcessStamp> _carried = [];
    // Names this registry's part of the note, so a write replaces only its own gits and another registry on the same folder keeps its own.
    private readonly Guid _key = Guid.NewGuid();
    private HashSet<ProcessStamp> _written = [];
    private Timer? _timer;
    private bool _closed;
    // Whether the file on disk is this registry's own writing; only that is this registry's to remove.
    private bool _wroteNote;
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
        _forApp[registry] = 0;
        if (Interlocked.Exchange(ref _exitHooked, 1) == 0)
        {
            AppDomain.CurrentDomain.ProcessExit += (_, _) =>
            {
                foreach (var each in _forApp.Keys)
                {
                    each.Close();
                }
            };
        }

        return registry;
    }

    /// <summary>Whether the registry is still on the list the process-exit handler closes.</summary>
    /// <param name="registry">A registry made by <see cref="ForApp"/>.</param>
    internal static bool IsClosedAtExit(GitProcessRegistry registry) => _forApp.ContainsKey(registry);

    /// <summary>Ends the registry's gits, removes its note, and takes it off the list the process-exit handler closes. Safe to call twice.</summary>
    public void Release()
    {
        Close();
        _forApp.TryRemove(this, out _);
    }

    private static GitProcessRegistry CreateShared()
    {
        var registry = new GitProcessRegistry();
        AppDomain.CurrentDomain.ProcessExit += (_, _) => registry.KillAll();
        return registry;
    }

    /// <summary>Notes a started process; the returned token takes it off the list again once its call is over.</summary>
    /// <param name="process">The process just started.</param>
    /// <param name="survivesExit">True for a git that is left to finish when SavePoint is stopped; the note of running git does not name it and a close does not end it.</param>
    public IDisposable Add(Process process, bool survivesExit = false) =>
        ProcessStamp.Of(process) is { } stamp ? Add(stamp, survivesExit) : Registration.None;

    /// <summary>Notes a process by its stamp. On a registry that is already closed, a process that would be ended at close is ended at once.</summary>
    /// <param name="stamp">The process.</param>
    /// <param name="survivesExit">True for a git that is left to finish when SavePoint is stopped; the note of running git does not name it and a close does not end it.</param>
    public IDisposable Add(ProcessStamp stamp, bool survivesExit = false)
    {
        _running[stamp.Id] = new Entry(stamp, survivesExit);
        Touch();

        // Listed first, then looked at: a close that began meanwhile either sees the entry or is seen here. The process is ended, not refused,
        // because the caller has already started it and a refusal would leave it running with nobody to end it.
        if (!survivesExit && IsClosed())
        {
            KillAll();
        }

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

    /// <summary>
    /// Ends every registered process except those left to finish, and writes no more of the note. Done as the process exits. What this registry
    /// wrote on the note is taken back to the gits of an earlier run that still run; what another registry on the same note wrote stays, and a
    /// note this registry never wrote is left alone, because it is another run's.
    /// </summary>
    public void Close()
    {
        bool wrote;
        lock (_noteGate)
        {
            _closed = true;
            _timer?.Dispose();
            _timer = null;
            wrote = _wroteNote;
        }

        KillAll();
        if (_notePath is null || !wrote)
        {
            return;
        }

        SettleNote();
    }

    /// <summary>Ends every registered process, except those left to finish, and what it started. A process that took over the number of one that ended is left alone.</summary>
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

            // A git left to finish (a reclaim, D46.1) is out of the kill-on-close job too; the next start waits for it by its own marker.
            if (entry.SurvivesExit)
            {
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

    private bool IsClosed()
    {
        lock (_noteGate)
        {
            return _closed;
        }
    }

    // Leaves this registry's part of the note holding only the gits of an earlier run that still run, which no registry of this run can end.
    private void SettleNote()
    {
        lock (_noteGate)
        {
            var carried = _carried.Where(git => git.IsRunning()).ToHashSet();
            _wroteNote = false;
            try
            {
                RunningGitNote.Update(_notePath!, ProcessStamp.Current(), _key, carried);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                _log?.Warning($"The note of running git at {_notePath} could not be settled.", e);
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

            var current = _running.Values.Where(entry => !entry.SurvivesExit).Select(entry => entry.Stamp).ToHashSet();
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
                RunningGitNote.Update(_notePath, ProcessStamp.Current(), _key, current);
                _wroteNote = current.Count > 0;
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

    private readonly record struct Entry(ProcessStamp Stamp, bool SurvivesExit);

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
