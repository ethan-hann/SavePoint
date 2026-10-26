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

    // Names git printed that no report has carried yet.
    private readonly List<string> _pending = [];

    // Names git printed while a group's add is under way. The index is written only when the add ends, so until then none of them is stored.
    private readonly List<string> _open = [];
    private int _openCount;
    private string? _openCurrent;
    private bool _inBatch;
    private int _reportedDone = -1;
    private int? _reportedTotal;
    private long _reportedAt;

    // A capture counts bytes by watching the object store; a switch counts them from its plan instead.
    private long? _byteTotal;
    private long _written;
    private long _reportedWritten = -1;

    // How long the store must hold still at its expected size before the wait is called finishing. git reads and hashes a whole file
    // under the big-file threshold before it writes it, and a slow disk or a scanner makes that take several seconds with no growth, so
    // the window is longer than the slowest such read (128 MiB at about 20 MB/s). The real wait lasts minutes, so this costs little.
    internal static readonly TimeSpan FinishingWindow = TimeSpan.FromSeconds(8);

    // How long a capture may go without a name or a detail before it says what it is storing.
    internal static readonly TimeSpan DetailInterval = TimeSpan.FromSeconds(20);

    private long _lastObserved = -1;
    private long _lastGrowthAt;
    private bool _finishing;
    private bool _reportedFinishing;

    // The big files of the add under way, in the order git takes them, with the running sum of their sizes before each, and where the
    // store stood when the add began.
    private IReadOnlyList<string>? _bigPaths;
    private long[] _bigStarts = [];
    private long[] _bigSizes = [];
    private long _bigTotal;
    private long _batchStart;

    // A detail waiting for the next report, and when a name or a detail last went out.
    private CopyDetail? _detail;
    private long _lastLineAt;

    private readonly TimeProvider _time;

    public ProgressReporter(IProgress<FileProgress>? sink, CheckoutBytePlan? bytes = null, TimeProvider? time = null)
    {
        _sink = sink;
        _bytes = bytes;
        _time = time ?? TimeProvider.System;
        _lastLineAt = _time.GetTimestamp();
    }

    public void SetTotal(int? total)
    {
        lock (_gate)
        {
            _total = total;
        }
    }

    // Files an earlier, stopped attempt already staged: git will not print them again, and the closing count must still include them.
    public void SetAlreadyDone(int count)
    {
        lock (_gate)
        {
            _done = Math.Max(_done, count);
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
            var now = _time.GetTimestamp();
            if (written != _lastObserved)
            {
                _lastObserved = written;
                _lastGrowthAt = now;
            }

            var finishing = !_finishing && total > 0 && written >= ceiling && _time.GetElapsedTime(_lastGrowthAt, now) >= FinishingWindow;
            if (next == _written && !finishing)
            {
                return;
            }

            _finishing |= finishing;
            _written = next;
            if (!_finishing && _time.GetElapsedTime(_lastLineAt, now) >= DetailInterval)
            {
                _detail = DetailNow(written);
            }

            Report();
        }
    }

    // The files are written and only the reading back is left, which moves no count: the report keeps the counts it ended with and says so.
    public void BeginWrapUp()
    {
        lock (_gate)
        {
            _finishing = true;
            Report();
        }
    }

    // A group of files is about to be handed to git. A single large file is named (and announced at once), since git says nothing about it until it is done.
    public void BeginBatch(IReadOnlyList<string>? bigPaths, IReadOnlyList<long>? bigSizes, long? storeNow)
    {
        lock (_gate)
        {
            // What was handled before this group is final, so it goes out now rather than being mistaken for the group's own names.
            if (_pending.Count > 0)
            {
                Report();
            }

            _inBatch = true;

            // The wait for the last add is over: a flag from it must not hide this one, and the store's stillness is counted afresh.
            var wasFinishing = _reportedFinishing;
            _finishing = false;
            _lastGrowthAt = _time.GetTimestamp();
            _batchStart = storeNow ?? Math.Max(_lastObserved, 0);
            _bigPaths = null;
            if (bigPaths is { Count: > 0 } && bigSizes is not null && bigSizes.Count == bigPaths.Count)
            {
                _bigPaths = bigPaths;
                _bigSizes = [.. bigSizes];
                _bigStarts = new long[bigSizes.Count];
                for (var i = 1; i < _bigStarts.Length; i++)
                {
                    _bigStarts[i] = _bigStarts[i - 1] + _bigSizes[i - 1];
                }

                _bigTotal = _bigSizes.Sum();
                _detail = new CopyDetail(bigPaths[0], 0, _bigSizes[0], Starting: true);
                Report();
            }
            else if (wasFinishing)
            {
                Report();
            }
        }
    }

    private void DiscardOpen()
    {
        _open.Clear();
        _openCount = 0;
        _openCurrent = null;
    }

    // The group's add has ended, so git has printed its names: they are delivered now, not at the next paced report.
    public void EndBatch()
    {
        lock (_gate)
        {
            _inBatch = false;
            if (_openCount > 0)
            {
                _done += _openCount;
                _current = _openCurrent;
                _pending.AddRange(_open);
                DiscardOpen();
            }

            if (_pending.Count > 0)
            {
                Report();
            }
        }
    }

    // What to say about a stretch with no names. During the add of the big files, the store's growth is laid over them in the order git
    // takes them, so the file named is an estimate (it drifts when git skips content it already holds); otherwise only the folder's figures are honest.
    private CopyDetail? DetailNow(long written)
    {
        if (_bigPaths is not null)
        {
            var at = Math.Clamp(written - _batchStart, 0, _bigTotal - _bigTotal / 100);
            var index = 0;
            while (index + 1 < _bigStarts.Length && _bigStarts[index + 1] <= at)
            {
                index++;
            }

            var size = _bigSizes[index];
            return new CopyDetail(_bigPaths[index], Math.Clamp(at - _bigStarts[index], 0, size - size / 100), size);
        }

        return _byteTotal is { } total ? new CopyDetail(null, _written, total) : null;
    }

    // The capture ended without a final report (failed or stopped): the wrap-up flag must not outlive the work.
    public void Abandon()
    {
        lock (_gate)
        {
            // A group git did not finish stored nothing, so its names are not reported as copied.
            DiscardOpen();
            _inBatch = false;
            _finishing = false;
            if (_reportedFinishing || _pending.Count > 0)
            {
                Report();
            }
        }
    }

    // One more file handled. Its name is kept for the next report whatever the pace; the reports themselves are the first at once, later ones at most ten times a second.
    public void FileHandled(string? path)
    {
        lock (_gate)
        {
            if (_inBatch)
            {
                _openCount++;
                _openCurrent = path;
                if (_sink is not null && !string.IsNullOrEmpty(path))
                {
                    _open.Add(path);
                }

                return;
            }

            _done++;
            _current = path;
            if (_sink is not null && !string.IsNullOrEmpty(path))
            {
                _pending.Add(path);
            }

            ReportIfDue();
        }
    }

    // Files and bytes written before git's own count starts (a switch that finishes a stopped one writes some files itself); git's counts
    // are added to them, so a count that starts below them does not take the bar back.
    private int _writtenFiles;
    private long _writtenBytes;

    // For output that carries its own counts.
    public void Observe(int done, int total)
    {
        lock (_gate)
        {
            _done = Math.Max(_done, _writtenFiles + done);
            _total = _writtenFiles + total;
            ReportIfDue();
        }
    }

    // The files written so far before git's count starts, for which git prints no meter: how many of how many, and how many bytes.
    public void ObserveFinishing(int done, int total, long bytes)
    {
        lock (_gate)
        {
            _writtenFiles = done;
            _writtenBytes = bytes;
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
            if ((_done > 0 && _done != _reportedDone) || (_byteTotal is not null && _written != _reportedWritten) || _reportedFinishing || _pending.Count > 0 || (_reportedDone >= 0 && _total != _reportedTotal))
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

    // The bytes the plan puts after this many files. With files written before git's count, those bytes are theirs and git's count covers
    // the rest of the plan.
    private long? BytesDone(int done, int? total)
    {
        if (_bytes is null)
        {
            return null;
        }

        if (_writtenFiles == 0)
        {
            return _bytes.BytesAfter(done, total ?? _bytes.Entries);
        }

        var remaining = Math.Max(0, _bytes.Total - _writtenBytes);
        return _writtenBytes + _bytes.BytesAfter(Math.Max(0, done - _writtenFiles), Math.Max(0, (total ?? _bytes.Entries) - _writtenFiles), remaining);
    }

    private void Report()
    {
        if (_sink is null)
        {
            return;
        }

        _reportedDone = _done;
        _reportedTotal = _total;
        _reportedWritten = _written;
        _reportedFinishing = _finishing;
        _reportedAt = Stopwatch.GetTimestamp();

        // Taken before the sink runs, so a sink that fails cannot make the next report repeat them.
        IReadOnlyList<string>? names = _pending.Count > 0 ? [.. _pending] : null;
        var detail = _detail;
        _pending.Clear();
        _detail = null;
        if (names is not null || detail is not null)
        {
            _lastLineAt = _time.GetTimestamp();
        }

        // Progress only decorates the work: a sink that fails must not stop git half way through a capture or a switch.
        try
        {
            var total = _total is { } counted ? Math.Max(counted, _done) : (int?)null;
            var report = _byteTotal is { } bytes
                ? new FileProgress(_done, total, _current, _written, bytes, _finishing, names, detail)
                : new FileProgress(_done, total, _current, BytesDone(_done, total), _bytes?.Total, _finishing, names, detail);
            _sink.Report(report);
        }
        catch (Exception)
        {
            // Dropped on purpose; the next report is tried as usual.
        }
    }
}
