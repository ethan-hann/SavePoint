using Savepoint.Core.Api;
using Savepoint.Core.Services.Jobs;

namespace Savepoint.Core.Tests.Services.Jobs;

internal static class JobRegistryTestKit
{
    public static readonly TimeSpan Generous = TimeSpan.FromSeconds(30);

    // State is read straight from the registry, so a test never depends on event timing to learn that a job ended.
    public static async Task<JobSnapshot> FinishedAsync(JobRegistry registry, Guid jobId)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        timeout.CancelAfter(Generous);
        while (true)
        {
            var job = registry.Get(jobId) ?? throw new InvalidOperationException($"Job {jobId} is not known.");
            if (job.Status != JobStatus.Running)
            {
                await registry.WhenDeliveredAsync();
                return job;
            }

            await Task.Delay(2, timeout.Token);
        }
    }

    // Snapshots are records, but their line lists are fresh copies, so the lines are compared by content.
    public static void AssertSame(JobSnapshot expected, JobSnapshot? actual)
    {
        Assert.NotNull(actual);
        Assert.Equal(expected with { RecentLines = [] }, actual with { RecentLines = [] });
        Assert.Equal(expected.RecentLines, actual.RecentLines);
    }

    public static async Task<JobSnapshot> StartAndFinishAsync(JobRegistry registry, JobResult? result = null)
    {
        var id = registry.Start(JobKind.Capture, "g", cancellable: true, _ => Task.FromResult(result));
        return await FinishedAsync(registry, id);
    }
}

// Collects every event a registry raises, in the order they arrive.
internal sealed class EventLog
{
    private readonly List<JobSnapshot> _events = [];

    public EventLog(JobRegistry registry)
    {
        registry.JobChanged += (_, snapshot) =>
        {
            lock (_events)
            {
                _events.Add(snapshot);
            }
        };
    }

    // Subscribes after everything already published has been delivered, so the log holds only what happens from here on.
    public static async Task<EventLog> FromNowAsync(JobRegistry registry)
    {
        await registry.WhenDeliveredAsync();
        return new EventLog(registry);
    }

    public IReadOnlyList<JobSnapshot> All
    {
        get
        {
            lock (_events)
            {
                return [.. _events];
            }
        }
    }

    public IReadOnlyList<JobSnapshot> For(Guid jobId) => [.. All.Where(e => e.JobId == jobId)];
}

internal sealed class Gate
{
    private readonly TaskCompletionSource _source = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task Task => _source.Task;

    public void Open() => _source.TrySetResult();
}

// A job whose work waits until the test says how it ends.
internal sealed class HeldJob
{
    private readonly TaskCompletionSource<JobContext> _context = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<JobResult?> _outcome = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public HeldJob(JobRegistry registry, JobKind kind = JobKind.Capture, string? gameId = "g", bool cancellable = true)
    {
        Id = registry.Start(kind, gameId, cancellable, async context =>
        {
            _context.TrySetResult(context);
            return await _outcome.Task;
        });
    }

    public Guid Id { get; }

    public Task<JobContext> Context => _context.Task.WaitAsync(JobRegistryTestKit.Generous, TestContext.Current.CancellationToken);

    public void Finish(JobResult? result = null) => _outcome.SetResult(result);

    public void Throw(Exception error) => _outcome.SetException(error);
}
