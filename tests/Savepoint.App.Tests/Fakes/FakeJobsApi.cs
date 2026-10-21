using Savepoint.Core.Api;
using Savepoint.Core.Errors;

namespace Savepoint.App.Tests.Fakes;

// Jobs never move on their own: a test reports progress and ends each one, so every run is deterministic.
public sealed class FakeJobsApi : IJobsApi
{
    private const string _api = nameof(IJobsApi);
    private const int _finishedKept = 5;
    private const int _recentLinesKept = 200;

    private static readonly HashSet<string> _cancelledIds =
        [.. Enum.GetValues<CancelledOperation>().Select(op => ErrorInfo.From(new OperationCancelledByUserException(op, null)).MessageId)];

    private readonly FakeBackend _backend;
    private readonly List<JobRecord> _jobs = [];
    private readonly List<Guid> _finishOrder = [];
    private long _sequence;

    internal FakeJobsApi(FakeBackend backend)
    {
        _backend = backend;
    }

    public event EventHandler<JobSnapshot>? JobChanged;

    // Every job ever started, in start order, including ones List() no longer keeps; not recorded as a call.
    public IReadOnlyList<JobSnapshot> Started => _backend.Peek<IReadOnlyList<JobSnapshot>>(() => [.. _jobs.Select(j => j.Snapshot)]);

    public IReadOnlyList<JobSnapshot> List() =>
        _backend.Read<IReadOnlyList<JobSnapshot>>(_api, nameof(List), [], () =>
            [.. _jobs.Where(j => j.Snapshot.Status == JobStatus.Running).Select(j => j.Snapshot), .. Kept().Select(id => Find(id).Snapshot)]);

    public JobSnapshot? Get(Guid jobId) =>
        _backend.Read(_api, nameof(Get), [jobId], () =>
            _jobs.Find(j => j.Snapshot.JobId == jobId) is { } job && (job.Snapshot.Status == JobStatus.Running || Kept().Contains(jobId))
                ? job.Snapshot
                : null);

    public Task<OperationResult<Unit>> CancelAsync(Guid jobId) =>
        _backend.Call(_api, nameof(CancelAsync), [jobId], () =>
        {
            if (_jobs.Find(j => j.Snapshot.JobId == jobId) is { Snapshot: { Status: JobStatus.Running, Cancellable: true, CancelRequested: false } } job)
            {
                Update(job, s => s with { CancelRequested = true });
            }

            return OperationResult.Success();
        });

    public JobSnapshot LastStarted(JobKind? kind = null) =>
        _backend.Peek(() => _jobs.LastOrDefault(j => kind is null || j.Snapshot.Kind == kind)?.Snapshot)
        ?? throw new InvalidOperationException($"No {kind?.ToString() ?? "job"} has been started.");

    // Progress for a job that already finished is dropped, the way a late report from a subprocess would be.
    public void ReportProgress(Guid jobId, JobProgress progress) => _backend.Script(() => ApplyProgress(jobId, progress));

    // Without a result, the job ends with what the call that started it would produce when everything works.
    public void Complete(Guid jobId, JobResult? result = null) => _backend.Script(() => Finish(jobId, result));

    public void Fail(Guid jobId, ErrorInfo error, JobResult? result = null) =>
        _backend.Script(() => FinishWith(Find(jobId), JobStatus.Failed, result, error));

    public void CompleteCancelled(Guid jobId) =>
        _backend.Script(() =>
        {
            var job = Find(jobId);
            FinishWith(job, JobStatus.Cancelled, null, CancelledError(job.Snapshot));
        });

    internal Guid Start(JobKind kind, string? gameId, JobResult? defaultResult, Action<JobSnapshot>? onFinished = null)
    {
        var id = new Guid($"00000000-0000-0000-0000-{++_sequence:D12}");
        var snapshot = new JobSnapshot(
            id,
            kind,
            gameId,
            JobStatus.Running,
            Cancellable: kind != JobKind.ClearData,
            CancelRequested: false,
            JobProgress.None,
            [],
            _backend.Clock.Now,
            FinishedAt: null,
            Result: null,
            Error: null,
            Revision: 1);
        var job = new JobRecord(snapshot, defaultResult, onFinished);
        _jobs.Add(job);
        _backend.Raise(() => JobChanged?.Invoke(this, snapshot));
        return id;
    }

    internal void ApplyProgress(Guid jobId, JobProgress progress)
    {
        var job = Find(jobId);
        if (job.Snapshot.Status != JobStatus.Running)
        {
            return;
        }

        var lines = job.Snapshot.RecentLines;
        if (progress.CurrentPath is { } path)
        {
            lines = [.. lines.Skip(Math.Max(0, lines.Count + 1 - _recentLinesKept)), path];
        }

        Update(job, s => s with { Progress = progress, RecentLines = lines });
    }

    internal void Finish(Guid jobId, JobResult? result)
    {
        var job = Find(jobId);
        result ??= job.DefaultResult;
        var (status, error) = Judge(result);
        FinishWith(job, status, result, error);
    }

    internal void FinishWith(Guid jobId, JobStatus status, JobResult? result, ErrorInfo? error) =>
        FinishWith(Find(jobId), status, result, error);

    // Per-folder work is judged per folder (D13.20): all fine succeeds, all failed fails, anything between is partial.
    private static (JobStatus Status, ErrorInfo? Error) Judge(JobResult? result)
    {
        if (result is ClearAllDataResult clear)
        {
            return clear.NotRemoved.Count == 0 ? (JobStatus.Succeeded, null) : (JobStatus.Partial, null);
        }

        IReadOnlyList<FolderOutcome> folders = result switch
        {
            CaptureResult capture => capture.Folders,
            OptimizeResult optimize => [.. optimize.Games.SelectMany(g => g.Folders)],
            _ => [],
        };
        var errors = folders.Where(f => !f.Ok).Select(f => f.Error!).ToList();
        if (errors.Count == 0)
        {
            return (JobStatus.Succeeded, null);
        }

        if (errors.Count < folders.Count)
        {
            return (JobStatus.Partial, null);
        }

        return errors.All(e => _cancelledIds.Contains(e.MessageId)) ? (JobStatus.Cancelled, errors[0]) : (JobStatus.Failed, errors[0]);
    }

    private static ErrorInfo CancelledError(JobSnapshot job)
    {
        var op = job.Kind switch
        {
            JobKind.Launch => CancelledOperation.Launch,
            JobKind.Capture => CancelledOperation.Capture,
            JobKind.Compact => CancelledOperation.Compaction,
            JobKind.RelocateStorage => CancelledOperation.StorageRelocation,
            _ => throw new InvalidOperationException($"A {job.Kind} job cannot be cancelled."),
        };
        return ErrorInfo.From(new OperationCancelledByUserException(op, job.GameId));
    }

    private void FinishWith(JobRecord job, JobStatus status, JobResult? result, ErrorInfo? error)
    {
        if (job.Snapshot.Status != JobStatus.Running)
        {
            throw new InvalidOperationException($"Job {job.Snapshot.JobId} already finished as {job.Snapshot.Status}.");
        }

        Update(job, s => s with { Status = status, Result = result, Error = error, FinishedAt = _backend.Clock.Now });
        _finishOrder.Add(job.Snapshot.JobId);
        job.OnFinished?.Invoke(job.Snapshot);
    }

    private void Update(JobRecord job, Func<JobSnapshot, JobSnapshot> change)
    {
        var next = change(job.Snapshot) with { Revision = job.Snapshot.Revision + 1 };
        job.Snapshot = next;
        _backend.Raise(() => JobChanged?.Invoke(this, next));
    }

    private JobRecord Find(Guid jobId) =>
        _jobs.Find(j => j.Snapshot.JobId == jobId) ?? throw new InvalidOperationException($"The fake never started job {jobId}.");

    private IEnumerable<Guid> Kept() => _finishOrder.TakeLast(_finishedKept);

    private sealed class JobRecord(JobSnapshot snapshot, JobResult? defaultResult, Action<JobSnapshot>? onFinished)
    {
        public JobSnapshot Snapshot { get; set; } = snapshot;

        public JobResult? DefaultResult { get; } = defaultResult;

        public Action<JobSnapshot>? OnFinished { get; } = onFinished;
    }
}
