using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;
using Savepoint.Core.Services.Jobs;
using Savepoint.Core.Services.Locking;
using static Savepoint.Core.Tests.Services.Jobs.JobRegistryTestKit;

namespace Savepoint.Core.Tests.Services.Jobs;

// D13.17: automatic optimization yields to a launch, capture or delete on the same game, and says it was stopped, not cancelled.
public class JobPreemptionTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void A_preempted_optimization_reports_its_own_message_and_the_game()
    {
        var error = JobOutcome.PreemptedError(JobKind.Compact, "game-1");

        Assert.Equal("error.compaction.preempted", error.MessageId);
        Assert.Equal("game-1", error.Args["gameId"]);
    }

    [Fact]
    public void A_preempted_routine_optimization_reports_the_same_message()
    {
        var error = JobOutcome.PreemptedError(JobKind.PackStorage, "game-1");

        Assert.Equal("error.compaction.preempted", error.MessageId);
        Assert.Equal("game-1", error.Args["gameId"]);
    }

    [Fact]
    public async Task A_running_routine_optimization_can_be_preempted_and_ends_cancelled_with_the_preempted_message()
    {
        var registry = new JobRegistry();
        var held = new HeldJob(registry, JobKind.PackStorage);
        await held.Context;

        var outcome = registry.Preempt(held.Id);
        held.Throw(new OperationCanceledException());
        var done = await FinishedAsync(registry, held.Id);

        Assert.Equal(JobCancelOutcome.Requested, outcome);
        Assert.Equal(JobStatus.Cancelled, done.Status);
        Assert.Equal("error.compaction.preempted", done.Error!.MessageId);
    }

    [Theory]
    [InlineData(JobKind.Launch)]
    [InlineData(JobKind.Capture)]
    [InlineData(JobKind.RelocateStorage)]
    [InlineData(JobKind.ClearData)]
    public void Only_optimization_is_ever_preempted(JobKind kind)
    {
        Assert.Throws<InvalidOperationException>(() => JobOutcome.PreemptedError(kind, "game-1"));
    }

    [Fact]
    public void An_optimization_where_every_folder_was_preempted_counts_as_stopped_not_failed()
    {
        var preempted = ErrorInfo.From(new CompactionPreemptedException("g"));
        var result = new OptimizeResult([new GameOptimizeOutcome("g", [new FolderOutcome(RepoRole.Install, preempted), new FolderOutcome(RepoRole.External, preempted)], null)]);

        var (status, error) = JobOutcome.Judge(result);

        Assert.Equal(JobStatus.Cancelled, status);
        Assert.Equal(preempted, error);
    }

    [Fact]
    public async Task Preempting_a_running_optimization_ends_it_cancelled_with_the_preempted_message()
    {
        var registry = new JobRegistry();
        var started = new Gate();
        var id = registry.Start(JobKind.Compact, "g", cancellable: true, async context =>
        {
            started.Open();
            await Task.Delay(Timeout.Infinite, context.CancellationToken);
            return null;
        });
        await started.Task.WaitAsync(Generous, Ct);

        var outcome = registry.Preempt(id);
        var done = await FinishedAsync(registry, id);

        Assert.Equal(JobCancelOutcome.Requested, outcome);
        Assert.Equal(JobStatus.Cancelled, done.Status);
        Assert.True(done.CancelRequested);
        Assert.Equal("error.compaction.preempted", done.Error!.MessageId);
        Assert.Equal("g", done.Error.Args["gameId"]);
    }

    [Fact]
    public async Task Preempting_raises_the_same_single_cancel_request_event_as_cancelling()
    {
        var registry = new JobRegistry();
        var held = new HeldJob(registry, JobKind.Compact);
        await held.Context;
        var events = await EventLog.FromNowAsync(registry);

        registry.Preempt(held.Id);
        await registry.WhenDeliveredAsync();

        var seen = Assert.Single(events.For(held.Id));
        Assert.True(seen.CancelRequested);
        Assert.Equal(JobStatus.Running, seen.Status);
        held.Finish();
        await FinishedAsync(registry, held.Id);
    }

    [Fact]
    public async Task A_job_that_is_not_an_optimization_is_not_preempted()
    {
        var registry = new JobRegistry();
        var held = new HeldJob(registry, JobKind.Capture);
        var context = await held.Context;

        var outcome = registry.Preempt(held.Id);

        Assert.Equal(JobCancelOutcome.NotCancellable, outcome);
        Assert.False(context.CancellationToken.IsCancellationRequested);
        Assert.False(registry.Get(held.Id)!.CancelRequested);
        held.Finish();
        await FinishedAsync(registry, held.Id);
    }

    [Fact]
    public async Task Preempting_an_unknown_or_finished_job_changes_nothing()
    {
        var registry = new JobRegistry();
        var done = await StartAndFinishAsync(registry);

        Assert.Equal(JobCancelOutcome.Unknown, registry.Preempt(Guid.NewGuid()));
        Assert.Equal(JobCancelOutcome.Finished, registry.Preempt(done.JobId));
        AssertSame(done, registry.Get(done.JobId));
    }

    [Fact]
    public async Task A_user_cancel_that_came_first_keeps_its_own_message()
    {
        var registry = new JobRegistry();
        var held = new HeldJob(registry, JobKind.Compact);
        await held.Context;
        registry.Cancel(held.Id);

        var outcome = registry.Preempt(held.Id);
        held.Throw(new OperationCanceledException());
        var done = await FinishedAsync(registry, held.Id);

        Assert.Equal(JobCancelOutcome.AlreadyRequested, outcome);
        Assert.Equal("error.compaction.cancelled", done.Error!.MessageId);
    }

    [Fact]
    public async Task A_preemption_that_came_first_is_not_turned_into_a_user_cancel()
    {
        var registry = new JobRegistry();
        var held = new HeldJob(registry, JobKind.Compact);
        await held.Context;
        registry.Preempt(held.Id);

        var outcome = registry.Cancel(held.Id);
        held.Throw(new OperationCanceledException());
        var done = await FinishedAsync(registry, held.Id);

        Assert.Equal(JobCancelOutcome.AlreadyRequested, outcome);
        Assert.Equal("error.compaction.preempted", done.Error!.MessageId);
    }

    [Fact]
    public async Task Work_that_reports_its_own_preemption_ends_cancelled_with_that_error()
    {
        var registry = new JobRegistry();

        var id = registry.Start(JobKind.Compact, "g", true, _ => throw new CompactionPreemptedException("g"));
        var done = await FinishedAsync(registry, id);

        Assert.Equal(JobStatus.Cancelled, done.Status);
        Assert.Equal("error.compaction.preempted", done.Error!.MessageId);
    }

    [Fact]
    public async Task A_request_on_the_same_game_stops_a_preemptible_optimization_and_then_runs()
    {
        var locks = new GameLock();
        var registry = new JobRegistry();
        var optimizing = new Gate();
        var id = registry.Start(JobKind.Compact, "g", cancellable: true, context =>
            locks.RunExclusiveAsync<JobResult?>("g", async handle =>
            {
                using var registration = handle.MarkPreemptible(() => registry.Preempt(context.JobId));
                optimizing.Open();
                await Task.Delay(Timeout.Infinite, context.CancellationToken);
                return null;
            }));
        await optimizing.Task.WaitAsync(Generous, Ct);

        var launched = await locks.RunExclusiveAsync("g", () => Task.FromResult("launch ran"), Ct).WaitAsync(Generous, Ct);
        var done = await FinishedAsync(registry, id);

        Assert.Equal("launch ran", launched);
        Assert.Equal(JobStatus.Cancelled, done.Status);
        Assert.Equal("error.compaction.preempted", done.Error!.MessageId);
    }
}
