using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using Savepoint.Core.Diagnostics;
using Savepoint.Core.Paths;
using Savepoint.Core.Platform;

namespace Savepoint.Core.Git;

// A SavePoint that is stopped from outside (Task Manager, a kill signal, a crash) can leave its git running, with the lock files and
// half-written packs of whatever it was doing. The startup check removes those files on the grounds that nothing of SavePoint's runs
// (D18), so it first looks for programs running from the bundled git's folder. SavePoint is a single instance and owns every git on its
// repositories (D31), so any such program that this run did not start belongs to a SavePoint that is gone.
internal sealed class BundledGitLeftovers
{
    private static readonly TimeSpan _defaultWait = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan _defaultKillWait = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan _defaultPoll = TimeSpan.FromMilliseconds(250);

    // The system reads start times from a clock that can be adjusted, so two reads of one moment may differ by a little.
    private static readonly TimeSpan _startTolerance = TimeSpan.FromSeconds(2);

    // A chain of parents longer than this is a loop made by reused numbers.
    private const int _maxAncestors = 64;

    private readonly IProcessInspector _processes;
    private readonly string[] _roots;
    private readonly StringComparison _comparison;
    private readonly IDiagnosticLog _log;
    private readonly Func<int, bool> _isRegistered;
    private readonly TimeSpan _wait;
    private readonly TimeSpan _killWait;
    private readonly TimeSpan _pollEvery;
    private readonly Action<RunningProcess> _kill;
    private readonly int _selfId = Environment.ProcessId;
    private readonly DateTimeOffset? _selfStarted;

    /// <param name="processes">Lists what runs on the machine.</param>
    /// <param name="bundleRoot">The folder of the bundled git; only programs whose image is inside it count.</param>
    /// <param name="pathComparison">How this OS compares paths.</param>
    /// <param name="log">Where the check says what it found and did.</param>
    /// <param name="isRegistered">Whether a process id is one of the gits this run started and has not seen end; those are never leftovers.</param>
    /// <param name="wait">How long leftovers get to end by themselves before they are stopped.</param>
    /// <param name="killWait">How long to wait for stopped leftovers to be gone.</param>
    /// <param name="pollEvery">How often the list is read while waiting.</param>
    /// <param name="kill">Stops a process and what it started; tests replace it.</param>
    public BundledGitLeftovers(
        IProcessInspector processes,
        string bundleRoot,
        StringComparison pathComparison,
        IDiagnosticLog log,
        Func<int, bool>? isRegistered = null,
        TimeSpan? wait = null,
        TimeSpan? killWait = null,
        TimeSpan? pollEvery = null,
        Action<RunningProcess>? kill = null)
    {
        _processes = processes;
        _comparison = pathComparison;
        _roots = RootsOf(bundleRoot, pathComparison);
        _log = log;
        _isRegistered = isRegistered ?? (_ => false);
        _wait = wait ?? _defaultWait;
        _killWait = killWait ?? _defaultKillWait;
        _pollEvery = pollEvery ?? _defaultPoll;
        _kill = kill ?? EndProcess;
        var stamp = ProcessStamp.Current();
        _selfStarted = stamp.StartedAtUtcTicks > 0 ? new DateTimeOffset(stamp.StartedAtUtcTicks, TimeSpan.Zero) : null;
    }

    /// <summary>The programs running from the bundle's folder that this run did not start and that do not descend from it.</summary>
    /// <remarks>
    /// Where the system gives no parent ids (macOS) or no image paths, only the registered gits are told apart from the rest, and where it
    /// gives no image path nothing is found: a process that cannot be placed is never touched.
    /// </remarks>
    public IReadOnlyList<RunningProcess> Find()
    {
        var all = _processes.List();
        var byId = new Dictionary<int, RunningProcess>();
        foreach (var process in all)
        {
            byId.TryAdd(process.Pid, process);
        }

        return [.. all.Where(process => process.Pid != _selfId && IsInBundle(process.ExecutablePath) && !IsOurs(process, byId))];
    }

    /// <summary>
    /// Gives leftovers a bounded time to end by themselves, then stops what remains. Nothing is logged or waited for when there are none.
    /// </summary>
    /// <param name="onWaiting">Called once, just before the wait begins, so a screen can say why nothing moves.</param>
    /// <param name="ct">Abandons the wait.</param>
    /// <returns>True when none is left running, so the files they used can be cleaned up; false when one could not be ended.</returns>
    public async Task<bool> EndAsync(Action? onWaiting, CancellationToken ct)
    {
        var found = Find();
        if (found.Count == 0)
        {
            return true;
        }

        _log.Information($"Startup check: found {Describe(found)}. They run from the bundled git and were left by a SavePoint that was stopped, so they get {_wait.TotalSeconds.ToString("0.#", CultureInfo.InvariantCulture)} s to end before they are stopped.");
        onWaiting?.Invoke();

        var remaining = await WaitUntilGoneAsync(_wait, ct).ConfigureAwait(false);
        if (remaining.Count == 0)
        {
            _log.Information("Startup check: the leftover git processes ended by themselves.");
            return true;
        }

        foreach (var process in remaining)
        {
            _kill(process);
        }

        remaining = await WaitUntilGoneAsync(_killWait, ct).ConfigureAwait(false);
        if (remaining.Count == 0)
        {
            _log.Warning($"Startup check: stopped the leftover git processes ({Describe(found)}).");
            return true;
        }

        _log.Warning($"Startup check: {Describe(remaining)} could not be ended, so no lock or temporary file is removed this time.");
        return false;
    }

    private async Task<IReadOnlyList<RunningProcess>> WaitUntilGoneAsync(TimeSpan limit, CancellationToken ct)
    {
        var watch = Stopwatch.StartNew();
        while (true)
        {
            var left = Find();
            if (left.Count == 0 || watch.Elapsed >= limit)
            {
                return left;
            }

            await Task.Delay(_pollEvery, ct).ConfigureAwait(false);
        }
    }

    // A program belongs to this run when it is one of the gits this run started, or sits below one or below this program itself. The
    // chain is read from the list's parent ids; a parent that started after its child is a number that was reused, not the parent.
    private bool IsOurs(RunningProcess process, Dictionary<int, RunningProcess> byId)
    {
        var current = process;
        for (var depth = 0; depth < _maxAncestors; depth++)
        {
            if (_isRegistered(current.Pid))
            {
                return true;
            }

            if (current.ParentPid is not { } parentId)
            {
                return false;
            }

            if (parentId == _selfId)
            {
                return !(current.StartedAt is { } started && _selfStarted is { } selfStarted && started < selfStarted - _startTolerance);
            }

            if (!byId.TryGetValue(parentId, out var parent) || (parent.StartedAt is { } parentStarted && current.StartedAt is { } childStarted && parentStarted > childStarted + _startTolerance))
            {
                return _isRegistered(parentId);
            }

            current = parent;
        }

        return false;
    }

    private bool IsInBundle(string? imagePath)
    {
        if (string.IsNullOrEmpty(imagePath))
        {
            return false;
        }

        string full;
        try
        {
            full = Path.GetFullPath(imagePath);
        }
        catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }

        // Compared with the separator, so a folder that only starts with the bundle's name (git2 beside git) is not inside it.
        return _roots.Any(root => full.StartsWith(root + Path.DirectorySeparatorChar, _comparison));
    }

    // The path as given and as the system really names it (a link in the way, or macOS's /var), since an image path is the real one.
    private static string[] RootsOf(string bundleRoot, StringComparison comparison)
    {
        var roots = new List<string> { Path.TrimEndingDirectorySeparator(Path.GetFullPath(bundleRoot)) };
        try
        {
            var real = Path.TrimEndingDirectorySeparator(RealPath.Resolve(roots[0]));
            if (!string.Equals(real, roots[0], comparison))
            {
                roots.Add(real);
            }
        }
        catch (Exception e) when (e is IOException or ArgumentException)
        {
            // Only the given spelling is known.
        }

        return [.. roots];
    }

    private static string Describe(IReadOnlyList<RunningProcess> processes) =>
        (processes.Count == 1 ? "1 leftover process" : $"{processes.Count} leftover processes") + ": " + string.Join(", ", processes.Select(process => $"{process.Name} ({process.Pid.ToString(CultureInfo.InvariantCulture)})"));

    private static void EndProcess(RunningProcess process)
    {
        try
        {
            using var running = Process.GetProcessById(process.Pid);
            ProcessTree.Kill(running);
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or Win32Exception)
        {
            // Ended on its own in the meantime.
        }
    }
}
