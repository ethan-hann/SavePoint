using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using Savepoint.Core.Diagnostics;

namespace Savepoint.Core.Git;

// A SavePoint that is stopped from outside (Task Manager, a kill signal, a crash) can leave its git running, with the lock files and
// half-written packs of whatever it was doing. The startup check removes those files on the grounds that nothing of SavePoint's runs (D18),
// so it first ends the gits the dead SavePoint noted (RunningGitNote). A git is the very process the note names only when its id and its
// start time both match, so another program that took over a number is never touched, and nothing is found by where a program runs from.
internal sealed class LeftoverGits
{
    private static readonly TimeSpan _defaultWait = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan _defaultKillWait = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan _defaultPoll = TimeSpan.FromMilliseconds(250);

    private readonly IDiagnosticLog _log;
    private readonly GitProcessRegistry? _registry;
    private readonly TimeSpan _wait;
    private readonly TimeSpan _killWait;
    private readonly TimeSpan _pollEvery;
    private readonly Action<ProcessStamp> _kill;
    private readonly List<ProcessStamp> _pending = [];

    /// <summary>Reads the note at once, before this run's own gits can rewrite it.</summary>
    /// <param name="notePath">Where the note of running git is kept.</param>
    /// <param name="log">Where the check says what it found and did.</param>
    /// <param name="registry">This run's registry, which keeps what could not be ended in its own note; none is carried when null.</param>
    /// <param name="wait">How long leftovers get to end by themselves before they are stopped.</param>
    /// <param name="killWait">How long to wait for stopped leftovers to be gone.</param>
    /// <param name="pollEvery">How often the leftovers are looked at while waiting.</param>
    /// <param name="kill">Stops a process and what it started; tests replace it.</param>
    public LeftoverGits(
        string notePath,
        IDiagnosticLog log,
        GitProcessRegistry? registry = null,
        TimeSpan? wait = null,
        TimeSpan? killWait = null,
        TimeSpan? pollEvery = null,
        Action<ProcessStamp>? kill = null)
    {
        _log = log;
        _registry = registry;
        _wait = wait ?? _defaultWait;
        _killWait = killWait ?? _defaultKillWait;
        _pollEvery = pollEvery ?? _defaultPoll;
        _kill = kill ?? (stamp => EndIfSame(stamp));

        // A note whose writer still runs is a live SavePoint's: what it names is its work, not a leftover.
        if (RunningGitNote.Read(notePath) is { } note && !note.Owner.IsRunning())
        {
            _pending.AddRange(note.Gits.Where(git => git.IsRunning()));
        }

        if (_pending.Count > 0)
        {
            registry?.Carry(_pending);
        }
    }

    /// <summary>The gits the note named that were still running when it was read.</summary>
    public IReadOnlyList<ProcessStamp> Pending => [.. _pending];

    /// <summary>Whether one of the gits the note named is still running, so what it may be using must be left alone.</summary>
    public bool AnyRunning() => _pending.Any(git => git.IsRunning());

    /// <summary>
    /// Gives leftovers a bounded time to end by themselves, then stops what remains. Nothing is logged or waited for when there are none.
    /// </summary>
    /// <param name="onWaiting">Called once, just before the wait begins, so a screen can say why nothing moves.</param>
    /// <param name="ct">Abandons the wait.</param>
    /// <returns>True when none is left running, so the files they used can be cleaned up; false when one could not be ended.</returns>
    public async Task<bool> EndAsync(Action? onWaiting, CancellationToken ct)
    {
        var found = Running();
        if (found.Count == 0)
        {
            return true;
        }

        _log.Information($"Startup check: found {Describe(found)}, left running by a SavePoint that was stopped, so they get {_wait.TotalSeconds.ToString("0.#", CultureInfo.InvariantCulture)} s to end before they are stopped.");
        onWaiting?.Invoke();

        var remaining = await WaitUntilGoneAsync(_wait, ct).ConfigureAwait(false);
        if (remaining.Count == 0)
        {
            _log.Information("Startup check: the leftover git processes ended by themselves.");
            _registry?.Carry([]);
            return true;
        }

        foreach (var git in remaining)
        {
            _kill(git);
        }

        remaining = await WaitUntilGoneAsync(_killWait, ct).ConfigureAwait(false);
        _registry?.Carry([]);
        if (remaining.Count == 0)
        {
            _log.Warning($"Startup check: stopped the leftover git processes ({Describe(found)}).");
            return true;
        }

        _log.Warning($"Startup check: {Describe(remaining)} could not be ended, so what they may be using is left alone this time.");
        return false;
    }

    /// <summary>
    /// Ends a process and what it started, but only when it started at the moment the stamp says, checked on the process just opened, so a
    /// number that was given to another program since the stamp was taken is never hit.
    /// </summary>
    /// <param name="stamp">The process to end.</param>
    /// <returns>True when the process was the one named and was ended; false when it is gone or is another program.</returns>
    public static bool EndIfSame(ProcessStamp stamp)
    {
        try
        {
            using var process = Process.GetProcessById(stamp.Id);
            if (!stamp.Matches(process))
            {
                return false;
            }

            ProcessTree.Kill(process);
            process.WaitForExit(5000);
            return true;
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or Win32Exception)
        {
            return false;
        }
    }

    private List<ProcessStamp> Running() => [.. _pending.Where(git => git.IsRunning())];

    private async Task<List<ProcessStamp>> WaitUntilGoneAsync(TimeSpan limit, CancellationToken ct)
    {
        var watch = Stopwatch.StartNew();
        while (true)
        {
            var left = Running();
            if (left.Count == 0 || watch.Elapsed >= limit)
            {
                return left;
            }

            await Task.Delay(_pollEvery, ct).ConfigureAwait(false);
        }
    }

    private static string Describe(List<ProcessStamp> gits) =>
        (gits.Count == 1 ? "1 leftover git process" : $"{gits.Count} leftover git processes") + " (" + string.Join(", ", gits.Select(git => git.Id.ToString(CultureInfo.InvariantCulture))) + ")";
}
