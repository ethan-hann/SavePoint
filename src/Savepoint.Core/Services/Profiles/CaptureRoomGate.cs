using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;
using Savepoint.Core.Git;

namespace Savepoint.Core.Services.Profiles;

/// <summary>
/// Judges the folders of one save together. They are saved side by side into one storage volume and each capture checks only its own files,
/// so each one reports what it would add once it has found room for that alone, and waits until every folder has either done the same or
/// dropped out (it has nothing to write, or it was refused). The folders that reported are then asked of the volume together. A folder that
/// cannot fit alone never reports, so the others still run and refuse or succeed on their own figures.
/// </summary>
internal sealed class CaptureRoomGate
{
    private readonly GitEngine _engine;
    private readonly IReadOnlyDictionary<RepoRole, RepoRef> _folders;
    private readonly HashSet<RepoRole> _undecided;
    private readonly Dictionary<RepoRole, Report> _reported = [];
    private readonly TaskCompletionSource _verdict = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly object _gate = new();

    /// <summary>Starts a gate over the folders of one save.</summary>
    /// <param name="engine">Asks the storage volume.</param>
    /// <param name="folders">Every folder the save will try to write.</param>
    public CaptureRoomGate(GitEngine engine, IReadOnlyList<RepoRef> folders)
    {
        _engine = engine;
        _folders = folders.ToDictionary(folder => folder.Role);
        _undecided = [.. _folders.Keys];

        // A stopped save can reach the verdict with every folder already gone from the wait, so nobody would read a failure it holds.
        _ = _verdict.Task.ContinueWith(
            static verdict => _ = verdict.Exception,
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    /// <summary>Reports what a folder would add and waits for the verdict on all the folders that reported.</summary>
    /// <param name="role">The folder.</param>
    /// <param name="newBytes">The bytes its capture would add.</param>
    /// <param name="cancellationToken">Stops the wait.</param>
    /// <param name="reduce">Finds the figure again once files are read for content the store already holds; called only when the folders' first figures together do not fit. Null when the figure cannot be reduced.</param>
    /// <exception cref="DiskSpaceException">The folders that reported cannot fit together, even with their figures reduced.</exception>
    public Task ReportAsync(RepoRole role, long newBytes, CancellationToken cancellationToken, Func<CancellationToken, Task<long>>? reduce = null)
    {
        lock (_gate)
        {
            if (_undecided.Remove(role))
            {
                _reported[role] = new Report(newBytes, reduce, cancellationToken);
                JudgeWhenDecided();
            }
        }

        return _verdict.Task.WaitAsync(cancellationToken);
    }

    /// <summary>Says a folder will not report: it has nothing to write, it was refused alone, or it failed. Safe to call more than once.</summary>
    /// <param name="role">The folder.</param>
    public void DropOut(RepoRole role)
    {
        lock (_gate)
        {
            if (_undecided.Remove(role))
            {
                JudgeWhenDecided();
            }
        }
    }

    // Called with the lock held, after a folder was decided.
    private void JudgeWhenDecided()
    {
        if (_undecided.Count > 0)
        {
            return;
        }

        // Finishes on the spot unless a figure has to be reduced, which waits on file reads without holding the lock.
        _ = JudgeAsync(_reported.ToArray());
    }

    private async Task JudgeAsync(KeyValuePair<RepoRole, Report>[] reports)
    {
        try
        {
            if (reports.Length > 1)
            {
                var repos = reports.Select(report => _folders[report.Key]).ToArray();
                try
                {
                    _engine.RequireCaptureRoom(repos, reports.Sum(report => report.Value.NewBytes));
                }
                catch (DiskSpaceException) when (reports.Any(report => report.Value.Reduce is not null))
                {
                    // Each folder passed on its raw size alone, so the sum may be refused where the content the store already holds would fit.
                    var reduced = 0L;
                    foreach (var (_, report) in reports)
                    {
                        reduced += report.Reduce is { } reduce ? await reduce(report.Cancellation).ConfigureAwait(false) : report.NewBytes;
                    }

                    _engine.RequireCaptureRoom(repos, reduced);
                }
            }

            _verdict.TrySetResult();
        }
        catch (Exception e)
        {
            // Whatever went wrong, the folders waiting on the verdict must be told rather than left waiting.
            _verdict.TrySetException(e);
        }
    }

    private readonly record struct Report(long NewBytes, Func<CancellationToken, Task<long>>? Reduce, CancellationToken Cancellation);
}
