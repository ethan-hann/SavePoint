using System.Collections.Concurrent;
using Savepoint.Core.Api;
using Savepoint.Core.Errors;

namespace Savepoint.Core.Services.Jobs;

/// <summary>
/// Runs long work as jobs: starts each on the thread pool, tracks its progress, cancellation and outcome, and
/// announces every change through <see cref="JobChanged"/>.
/// </summary>
/// <remarks>
/// Keeps the running jobs and the five most recently finished ones in memory; nothing is persisted. The registry takes
/// no per-game lock, so jobs for one game run side by side and order their git work with <c>GameLock</c>. Thread-safe.
/// </remarks>
public sealed class JobRegistry : IJobsApi
{
    private const int _finishedKept = 5;
    private const int _recentLinesKept = 200;

    // About twenty progress events a second per job.
    private static readonly TimeSpan _progressInterval = TimeSpan.FromMilliseconds(50);

    private readonly Lock _gate = new();
    private readonly TimeProvider _time;
    private readonly Dictionary<Guid, Job> _byId = [];
    private readonly List<Job> _running = [];
    private readonly Queue<Job> _finished = new();
    private readonly ConcurrentQueue<Delivery> _outbox = new();
    private int _delivering;

    /// <summary>Creates an empty registry.</summary>
    /// <param name="timeProvider">The clock for start and finish times and for the progress throttle; defaults to the system clock.</param>
    public JobRegistry(TimeProvider? timeProvider = null)
    {
        _time = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// Raised with the whole snapshot after a job starts, is cancelled, reports progress, or ends. Events are delivered in
    /// order, one at a time, on a thread-pool thread, never from inside the call that caused them, so a slow or failing
    /// handler delays other events but never the work. Progress events are coalesced to about twenty a second per job;
    /// the start, the cancel request and the end are never held back, and the end carries the latest progress.
    /// </summary>
    public event EventHandler<JobSnapshot>? JobChanged;

    /// <summary>Starts a job and returns at once; the work runs on the thread pool.</summary>
    /// <param name="kind">What the job does.</param>
    /// <param name="gameId">The game the job is for, or null for a job that spans every game.</param>
    /// <param name="cancellable">Whether <see cref="Cancel"/> may stop the job; a clear-data job never can (D13.18).</param>
    /// <param name="work">
    /// The work. It receives the job's context and returns the result, or null for work with none. An exception it throws
    /// ends the job as failed; one caused by the job's own cancellation ends it as cancelled.
    /// </param>
    /// <returns>The id of the new job.</returns>
    /// <exception cref="ArgumentException">A clear-data job was started as cancellable.</exception>
    public Guid Start(JobKind kind, string? gameId, bool cancellable, Func<JobContext, Task<JobResult?>> work)
    {
        ArgumentNullException.ThrowIfNull(work);
        if (cancellable && kind == JobKind.ClearData)
        {
            throw new ArgumentException("A clear-data job cannot be cancellable.", nameof(cancellable));
        }

        var job = new Job(Guid.NewGuid(), kind, gameId, cancellable, _time.GetUtcNow());
        lock (_gate)
        {
            _byId.Add(job.Id, job);
            _running.Add(job);
            Publish(job);
        }

        ScheduleDelivery();
        _ = Task.Run(() => RunAsync(job, work), CancellationToken.None);
        return job.Id;
    }

    /// <summary>Lists the running jobs in start order, then the five most recently finished in finish order.</summary>
    public IReadOnlyList<JobSnapshot> List()
    {
        lock (_gate)
        {
            return [.. _running.Select(Snapshot), .. _finished.Select(Snapshot)];
        }
    }

    /// <summary>Looks up a running job or one of the five most recently finished.</summary>
    /// <returns>The job's current snapshot, or null when it is unknown or has been pruned.</returns>
    public JobSnapshot? Get(Guid jobId)
    {
        lock (_gate)
        {
            return _byId.TryGetValue(jobId, out var job) ? Snapshot(job) : null;
        }
    }

    /// <summary>Asks a job to stop: marks it cancel-requested, raises an event, and cancels its token.</summary>
    /// <returns>
    /// What happened. Only <see cref="JobCancelOutcome.Requested"/> changes anything; an unknown, finished, already
    /// cancelled or non-cancellable job is left alone. Never throws; the job still ends through its own work.
    /// </returns>
    public JobCancelOutcome Cancel(Guid jobId)
    {
        CancellationTokenSource source;
        lock (_gate)
        {
            if (!_byId.TryGetValue(jobId, out var job))
            {
                return JobCancelOutcome.Unknown;
            }

            if (job.Status != JobStatus.Running)
            {
                return JobCancelOutcome.Finished;
            }

            if (!job.Cancellable)
            {
                return JobCancelOutcome.NotCancellable;
            }

            if (job.CancelRequested)
            {
                return JobCancelOutcome.AlreadyRequested;
            }

            job.CancelRequested = true;
            job.Revision++;
            Publish(job);
            source = job.Cancellation;
        }

        ScheduleDelivery();
        _ = CancelQuietlyAsync(source);
        return JobCancelOutcome.Requested;
    }

    /// <summary>The <see cref="IJobsApi"/> form of <see cref="Cancel"/>: always succeeds, whatever the job's state.</summary>
    public Task<OperationResult<Unit>> CancelAsync(Guid jobId)
    {
        _ = Cancel(jobId);
        return Task.FromResult(OperationResult.Success());
    }

    // The token flips before this returns, but the work's cancel callbacks (killing a process tree, say) run off the caller's thread.
    private static async Task CancelQuietlyAsync(CancellationTokenSource source)
    {
        try
        {
            await source.CancelAsync().ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
            // The job ended between the request and the cancel, which is the same as the cancel arriving late.
        }
        catch (AggregateException)
        {
            // A callback the work registered threw; the token is cancelled all the same.
        }
    }

    // Completes once every event raised before the call has been delivered; tests use it to know nothing more is coming.
    internal Task WhenDeliveredAsync()
    {
        var barrier = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _outbox.Enqueue(new Delivery(null, barrier));
        ScheduleDelivery();
        return barrier.Task;
    }

    private async Task RunAsync(Job job, Func<JobContext, Task<JobResult?>> work)
    {
        JobResult? result = null;
        JobStatus status;
        ErrorInfo? error;
        try
        {
            var context = new JobContext(job.Id, job.Cancellation.Token, progress => Report(job, progress, null), line => Report(job, null, line));
            result = await work(context).ConfigureAwait(false);
            (status, error) = JobOutcome.Judge(result);
        }
        catch (OperationCancelledByUserException cancelled)
        {
            result = null;
            (status, error) = (JobStatus.Cancelled, ErrorInfo.From(cancelled));
        }
        catch (OperationCanceledException) when (IsCancelRequested(job))
        {
            result = null;
            (status, error) = (JobStatus.Cancelled, JobOutcome.CancelledError(job.Kind, job.GameId));
        }
        catch (Exception exception)
        {
            result = null;
            (status, error) = (JobStatus.Failed, ErrorInfo.From(exception));
        }

        Complete(job, status, result, error);
    }

    private bool IsCancelRequested(Job job)
    {
        lock (_gate)
        {
            return job.CancelRequested;
        }
    }

    private void Complete(Job job, JobStatus status, JobResult? result, ErrorInfo? error)
    {
        lock (_gate)
        {
            job.Status = status;
            job.Result = result;
            job.Error = error;
            job.FinishedAt = _time.GetUtcNow();
            job.Revision++;
            Publish(job);
            _running.Remove(job);
            _finished.Enqueue(job);
            while (_finished.Count > _finishedKept)
            {
                _byId.Remove(_finished.Dequeue().Id);
            }
        }

        job.Cancellation.Dispose();
        ScheduleDelivery();
    }

    private void Report(Job job, JobProgress? progress, string? line)
    {
        var hasLine = !string.IsNullOrEmpty(line);
        if (progress is null && !hasLine)
        {
            return;
        }

        var published = false;
        lock (_gate)
        {
            // A subprocess still flushing output cannot bring a finished job back.
            if (job.Status != JobStatus.Running)
            {
                return;
            }

            if (progress is not null)
            {
                job.Progress = progress;
                AddLine(job, progress.CurrentPath);
            }

            if (hasLine)
            {
                AddLine(job, line);
            }

            job.Revision++;
            published = PublishOrDefer(job);
        }

        if (published)
        {
            ScheduleDelivery();
        }
    }

    private static void AddLine(Job job, string? line)
    {
        if (string.IsNullOrEmpty(line))
        {
            return;
        }

        job.Lines.Enqueue(line);
        while (job.Lines.Count > _recentLinesKept)
        {
            job.Lines.Dequeue();
        }
    }

    // The caller holds the gate. Sends the job's state now if its last event is old enough, otherwise arms a timer that does.
    private bool PublishOrDefer(Job job)
    {
        var now = _time.GetTimestamp();
        var sinceLast = job.LastPublished is { } last ? _time.GetElapsedTime(last, now) : TimeSpan.MaxValue;
        if (sinceLast >= _progressInterval)
        {
            Publish(job);
            return true;
        }

        if (job.Trailing is null)
        {
            var flush = new TrailingFlush(this, job);
            flush.Timer = _time.CreateTimer(static state => ((TrailingFlush)state!).Owner.FlushTrailing((TrailingFlush)state!), flush, _progressInterval - sinceLast, Timeout.InfiniteTimeSpan);
            job.Trailing = flush;
        }

        return false;
    }

    private void FlushTrailing(TrailingFlush flush)
    {
        lock (_gate)
        {
            var job = flush.Job;

            // A flush that lost the race with a publish or the end finds itself replaced or nothing left to say.
            if (!ReferenceEquals(job.Trailing, flush))
            {
                return;
            }

            job.Trailing = null;
            flush.Timer!.Dispose();
            if (job.Status != JobStatus.Running || job.PublishedRevision == job.Revision)
            {
                return;
            }

            Publish(job);
        }

        ScheduleDelivery();
    }

    // The caller holds the gate; the snapshot is queued under it, so events leave in the order changes were made.
    private void Publish(Job job)
    {
        job.PublishedRevision = job.Revision;
        job.LastPublished = _time.GetTimestamp();
        if (job.Trailing is { } trailing)
        {
            job.Trailing = null;
            trailing.Timer!.Dispose();
        }

        _outbox.Enqueue(new Delivery(Snapshot(job), null));
    }

    private static JobSnapshot Snapshot(Job job) =>
        new(
            job.Id,
            job.Kind,
            job.GameId,
            job.Status,
            job.Cancellable,
            job.CancelRequested,
            job.Progress,
            [.. job.Lines],
            job.StartedAt,
            job.FinishedAt,
            job.Result,
            job.Error,
            job.Revision);

    private void ScheduleDelivery()
    {
        if (Interlocked.CompareExchange(ref _delivering, 1, 0) != 0)
        {
            return;
        }

        ThreadPool.UnsafeQueueUserWorkItem(static registry => registry.Deliver(), this, preferLocal: false);
    }

    private void Deliver()
    {
        while (true)
        {
            while (_outbox.TryDequeue(out var delivery))
            {
                if (delivery.Barrier is { } barrier)
                {
                    barrier.TrySetResult();
                }
                else
                {
                    Raise(delivery.Snapshot!);
                }
            }

            Interlocked.Exchange(ref _delivering, 0);

            // Something may have been queued after the last dequeue and before the flag cleared.
            if (_outbox.IsEmpty || Interlocked.CompareExchange(ref _delivering, 1, 0) != 0)
            {
                return;
            }
        }
    }

    private void Raise(JobSnapshot snapshot)
    {
        if (JobChanged is not { } handlers)
        {
            return;
        }

        foreach (var handler in handlers.GetInvocationList().Cast<EventHandler<JobSnapshot>>())
        {
            try
            {
                handler(this, snapshot);
            }
            catch (Exception)
            {
                // A subscriber's failure must reach neither the work nor the other subscribers.
            }
        }
    }

    private readonly record struct Delivery(JobSnapshot? Snapshot, TaskCompletionSource? Barrier);

    private sealed class TrailingFlush(JobRegistry owner, Job job)
    {
        public JobRegistry Owner { get; } = owner;

        public Job Job { get; } = job;

        public ITimer? Timer { get; set; }
    }

    private sealed class Job(Guid id, JobKind kind, string? gameId, bool cancellable, DateTimeOffset startedAt)
    {
        public Guid Id { get; } = id;

        public JobKind Kind { get; } = kind;

        public string? GameId { get; } = gameId;

        public bool Cancellable { get; } = cancellable;

        public DateTimeOffset StartedAt { get; } = startedAt;

        public CancellationTokenSource Cancellation { get; } = new();

        public JobStatus Status { get; set; } = JobStatus.Running;

        public bool CancelRequested { get; set; }

        public JobProgress Progress { get; set; } = JobProgress.None;

        public Queue<string> Lines { get; } = new();

        public DateTimeOffset? FinishedAt { get; set; }

        public JobResult? Result { get; set; }

        public ErrorInfo? Error { get; set; }

        public long Revision { get; set; } = 1;

        public long PublishedRevision { get; set; }

        public long? LastPublished { get; set; }

        public TrailingFlush? Trailing { get; set; }
    }
}
