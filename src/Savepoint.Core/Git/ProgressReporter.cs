using System.Diagnostics;

namespace Savepoint.Core.Git;

// Turns git's per-file output into a few reports a second. Lines arrive on a background thread, one after another.
internal sealed class ProgressReporter
{
    private static readonly long _interval = Stopwatch.Frequency / 10;

    private readonly IProgress<FileProgress>? _sink;
    private readonly object _gate = new();
    private int _done;
    private int? _total;
    private string? _current;
    private int _reportedDone = -1;
    private long _reportedAt;

    public ProgressReporter(IProgress<FileProgress>? sink)
    {
        _sink = sink;
    }

    public void SetTotal(int? total)
    {
        lock (_gate)
        {
            _total = total;
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
            if (_done > 0 && _done != _reportedDone)
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
        _reportedAt = Stopwatch.GetTimestamp();
        _sink.Report(new FileProgress(_done, _total is { } total ? Math.Max(total, _done) : null, _current));
    }
}
