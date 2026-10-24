using System.Diagnostics;

namespace Savepoint.Core.Git;

// Turns git's per-file output into a few reports a second. Lines arrive on a background thread, one after another.
internal sealed class ProgressReporter
{
    private static readonly long _interval = Stopwatch.Frequency / 10;

    private readonly IProgress<FileProgress>? _sink;
    private readonly CheckoutBytePlan? _bytes;
    private readonly object _gate = new();
    private int _done;
    private int? _total;
    private string? _current;
    private int _reportedDone = -1;
    private long _reportedAt;

    // A capture counts bytes by watching the object store; a switch counts them from its plan instead.
    private long? _byteTotal;
    private long _written;
    private long _reportedWritten = -1;

    // Samples come twice a second, so this is a second of no growth.
    private const int _steadySamplesToFinish = 2;
    private long _lastObserved = -1;
    private int _steady;
    private bool _finishing;
    private bool _reportedFinishing;

    public ProgressReporter(IProgress<FileProgress>? sink, CheckoutBytePlan? bytes = null)
    {
        _sink = sink;
        _bytes = bytes;
    }

    public void SetTotal(int? total)
    {
        lock (_gate)
        {
            _total = total;
        }
    }

    // The bytes the capture has to store in all; reports carry them from then on.
    public void SetByteTotal(long total)
    {
        lock (_gate)
        {
            _byteTotal = Math.Max(total, 0);
        }
    }

    // Delivers the totals at once, so a screen has something to fill towards before the first file is done.
    public void Start()
    {
        lock (_gate)
        {
            Report();
        }
    }

    // Bytes git has put in the store so far. A report is made for each change, because the caller already paces them; the figure
    // never goes back, and stays under the total until Complete, since the store's size is only an estimate of the source bytes.
    public void ObserveWritten(long written)
    {
        lock (_gate)
        {
            if (_byteTotal is not { } total)
            {
                return;
            }

            var ceiling = total - total / 100;
            var next = Math.Max(_written, Math.Min(written, ceiling));

            // Git keeps working after it has stored the last byte (it closes its last pack and writes the index), and shows nothing for
            // it. A store that has reached its expected size and then holds still is that wait, so the report says so.
            _steady = written == _lastObserved ? _steady + 1 : 0;
            _lastObserved = written;
            var finishing = !_finishing && total > 0 && written >= ceiling && _steady >= _steadySamplesToFinish;
            if (next == _written && !finishing)
            {
                return;
            }

            _finishing |= finishing;
            _written = next;
            Report();
        }
    }

    // One more file handled; the first file is reported at once, later ones at most ten times a second.
    public void FileHandled(string? path)
    {
        lock (_gate)
        {
            _done++;
            _current = path;
            ReportIfDue();
        }
    }

    // For output that carries its own counts.
    public void Observe(int done, int total)
    {
        lock (_gate)
        {
            _done = Math.Max(_done, done);
            _total = total;
            ReportIfDue();
        }
    }

    // Delivers the final state, so the last report always shows everything that was done.
    public void Complete()
    {
        lock (_gate)
        {
            if (_byteTotal is { } total)
            {
                _written = total;
            }

            _finishing = false;
            if ((_done > 0 && _done != _reportedDone) || (_byteTotal is not null && _written != _reportedWritten) || _reportedFinishing)
            {
                Report();
            }
        }
    }

    private void ReportIfDue()
    {
        if (_reportedDone < 0 || Stopwatch.GetTimestamp() - _reportedAt >= _interval)
        {
            Report();
        }
    }

    private void Report()
    {
        if (_sink is null)
        {
            return;
        }

        _reportedDone = _done;
        _reportedWritten = _written;
        _reportedFinishing = _finishing;
        _reportedAt = Stopwatch.GetTimestamp();

        // Progress only decorates the work: a sink that fails must not stop git half way through a capture or a switch.
        try
        {
            var total = _total is { } counted ? Math.Max(counted, _done) : (int?)null;
            var report = _byteTotal is { } bytes
                ? new FileProgress(_done, total, _current, _written, bytes, _finishing)
                : new FileProgress(_done, total, _current, _bytes?.BytesAfter(_done, total ?? _bytes.Entries), _bytes?.Total);
            _sink.Report(report);
        }
        catch (Exception)
        {
            // Dropped on purpose; the next report is tried as usual.
        }
    }
}
