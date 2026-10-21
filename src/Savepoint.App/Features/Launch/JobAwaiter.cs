using Savepoint.Core.Api;

namespace Savepoint.App.Features.Launch;

/// <summary>
/// Lets view models wait for a job to end. It follows <see cref="IJobsApi.JobChanged"/> and completes each waiter with
/// the job's final snapshot, so a dialog can start a capture and carry on once it is done without polling. Waiters
/// resume on the thread that raised the event unless the awaiting code has a synchronization context, as the UI thread does.
/// </summary>
public sealed class JobAwaiter : IDisposable
{
    private readonly IJobsApi _jobs;
    private readonly Lock _gate = new();
    private readonly Dictionary<Guid, List<TaskCompletionSource<JobSnapshot>>> _waiting = [];

    /// <param name="jobs">The jobs Api whose events end the waits.</param>
    public JobAwaiter(IJobsApi jobs)
    {
        _jobs = jobs;
        _jobs.JobChanged += OnJobChanged;
    }

    /// <summary>Completes with the job's final snapshot: at once when it has already ended, otherwise when it does.</summary>
    /// <param name="jobId">The job to wait for.</param>
    public Task<JobSnapshot> WaitAsync(Guid jobId)
    {
        // No RunContinuationsAsynchronously: a waiter on the UI thread resumes there, and a test's scripted event resumes it inline.
        var waiter = new TaskCompletionSource<JobSnapshot>();
        lock (_gate)
        {
            if (!_waiting.TryGetValue(jobId, out var list))
            {
                _waiting[jobId] = list = [];
            }

            list.Add(waiter);
        }

        // The waiter is registered before the snapshot is read, so an end between the two is not lost.
        if (_jobs.Get(jobId) is { Status: not JobStatus.Running } ended)
        {
            Release(ended);
        }

        return waiter.Task;
    }

    /// <summary>Stops following job events; pending waits never complete.</summary>
    public void Dispose() => _jobs.JobChanged -= OnJobChanged;

    private void OnJobChanged(object? sender, JobSnapshot job)
    {
        if (job.Status != JobStatus.Running)
        {
            Release(job);
        }
    }

    private void Release(JobSnapshot job)
    {
        List<TaskCompletionSource<JobSnapshot>>? waiters;
        lock (_gate)
        {
            _waiting.Remove(job.JobId, out waiters);
        }

        if (waiters is null)
        {
            return;
        }

        foreach (var waiter in waiters)
        {
            waiter.TrySetResult(job);
        }
    }
}
