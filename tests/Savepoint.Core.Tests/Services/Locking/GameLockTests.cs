using Savepoint.Core.Services.Locking;

namespace Savepoint.Core.Tests.Services.Locking;

public class GameLockTests
{
    private static readonly TimeSpan _generous = TimeSpan.FromSeconds(30);
    // A request that should stay behind a holder would get in within moments, and only waiting shows that it does not. The same pause lets a queued request reach the lock.
    private static readonly TimeSpan _grace = TimeSpan.FromMilliseconds(150);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task The_result_of_the_function_is_returned()
    {
        var locks = new GameLock();

        var value = await locks.RunExclusiveAsync("g", () => Task.FromResult(42), Ct);

        Assert.Equal(42, value);
    }

    [Fact]
    public async Task Calls_on_one_key_run_one_at_a_time()
    {
        var locks = new GameLock();
        var first = new Gate();
        var secondStarted = new Gate();
        var firstStarted = new Gate();

        var a = locks.RunExclusiveAsync("g", async () =>
        {
            firstStarted.Open();
            await first.Task;
        }, Ct);
        await firstStarted.Task.WaitAsync(_generous, Ct);
        var b = locks.RunExclusiveAsync("g", () =>
        {
            secondStarted.Open();
            return Task.CompletedTask;
        }, Ct);

        await AssertStaysPending(secondStarted.Task, "the second call must wait for the first");
        first.Open();
        await a.WaitAsync(_generous, Ct);
        await b.WaitAsync(_generous, Ct);
        Assert.True(secondStarted.Task.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task Calls_on_one_key_run_in_call_order()
    {
        var locks = new GameLock();
        var order = new List<int>();
        var holding = new Gate();
        var release = new Gate();

        var holder = locks.RunExclusiveAsync("g", async () =>
        {
            holding.Open();
            await release.Task;
        }, Ct);
        await holding.Task.WaitAsync(_generous, Ct);
        var queued = Enumerable.Range(1, 20)
            .Select(n => locks.RunExclusiveAsync("g", () =>
            {
                lock (order)
                {
                    order.Add(n);
                }

                return Task.CompletedTask;
            }, Ct))
            .ToList();

        release.Open();
        await holder.WaitAsync(_generous, Ct);
        await Task.WhenAll(queued).WaitAsync(_generous, Ct);

        Assert.Equal(Enumerable.Range(1, 20), order);
    }

    [Fact]
    public async Task Different_keys_never_wait_on_each_other()
    {
        var locks = new GameLock();
        var holding = new Gate();
        var release = new Gate();
        var held = locks.RunExclusiveAsync("one", async () =>
        {
            holding.Open();
            await release.Task;
        }, Ct);
        await holding.Task.WaitAsync(_generous, Ct);

        var other = await locks.RunExclusiveAsync("two", () => Task.FromResult("done"), Ct).WaitAsync(_generous, Ct);

        Assert.Equal("done", other);
        Assert.False(held.IsCompleted);
        release.Open();
        await held.WaitAsync(_generous, Ct);
    }

    [Fact]
    public async Task A_function_that_throws_does_not_block_the_next_call_and_the_error_reaches_its_own_caller()
    {
        var locks = new GameLock();
        var holding = new Gate();
        var release = new Gate();
        var failing = locks.RunExclusiveAsync<int>("g", async () =>
        {
            holding.Open();
            await release.Task;
            throw new InvalidOperationException("boom");
        }, Ct);
        await holding.Task.WaitAsync(_generous, Ct);
        var next = locks.RunExclusiveAsync("g", () => Task.FromResult("next"), Ct);

        release.Open();

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => failing.WaitAsync(_generous, Ct));
        Assert.Equal("boom", error.Message);
        Assert.Equal("next", await next.WaitAsync(_generous, Ct));
    }

    [Fact]
    public async Task A_function_that_throws_before_its_first_await_still_releases_the_lock()
    {
        var locks = new GameLock();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => locks.RunExclusiveAsync<int>("g", () => throw new InvalidOperationException("sync"), Ct));

        Assert.Equal(7, await locks.RunExclusiveAsync("g", () => Task.FromResult(7), Ct).WaitAsync(_generous, Ct));
    }

    [Fact]
    public async Task A_key_is_removed_once_its_last_call_has_finished()
    {
        var locks = new GameLock();
        var holding = new Gate();
        var release = new Gate();
        var first = locks.RunExclusiveAsync("g", async () =>
        {
            holding.Open();
            await release.Task;
        }, Ct);
        await holding.Task.WaitAsync(_generous, Ct);
        var second = locks.RunExclusiveAsync("g", () => Task.CompletedTask, Ct);
        _ = locks.RunExclusiveAsync("h", () => Task.CompletedTask, Ct);
        await Task.Delay(_grace, Ct);

        Assert.Equal(1, locks.ActiveKeyCount);
        release.Open();
        await first.WaitAsync(_generous, Ct);
        await second.WaitAsync(_generous, Ct);

        Assert.Equal(0, locks.ActiveKeyCount);
    }

    [Fact]
    public async Task A_key_is_removed_after_a_function_throws()
    {
        var locks = new GameLock();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => locks.RunExclusiveAsync<int>("g", () => throw new InvalidOperationException(), Ct));

        Assert.Equal(0, locks.ActiveKeyCount);
    }

    [Fact]
    public async Task A_waiting_call_honors_cancellation_and_never_runs()
    {
        var locks = new GameLock();
        var holding = new Gate();
        var release = new Gate();
        var ran = false;
        var holder = locks.RunExclusiveAsync("g", async () =>
        {
            holding.Open();
            await release.Task;
        }, Ct);
        await holding.Task.WaitAsync(_generous, Ct);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var waiting = locks.RunExclusiveAsync("g", () =>
        {
            ran = true;
            return Task.CompletedTask;
        }, cts.Token);

        await cts.CancelAsync();

        var cancelled = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting.WaitAsync(_generous, Ct));
        Assert.Equal(cts.Token, cancelled.CancellationToken);
        release.Open();
        await holder.WaitAsync(_generous, Ct);
        Assert.False(ran);
        Assert.Equal(0, locks.ActiveKeyCount);
    }

    [Fact]
    public async Task A_token_that_is_already_cancelled_runs_nothing_and_takes_no_lock()
    {
        var locks = new GameLock();
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var ran = false;

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => locks.RunExclusiveAsync("g", () =>
        {
            ran = true;
            return Task.CompletedTask;
        }, cts.Token));

        Assert.False(ran);
        Assert.Equal(0, locks.ActiveKeyCount);
        Assert.Equal(1, await locks.RunExclusiveAsync("g", () => Task.FromResult(1), Ct).WaitAsync(_generous, Ct));
    }

    [Fact]
    public async Task A_cancelled_waiter_leaves_the_queue_and_the_rest_keep_their_order()
    {
        var locks = new GameLock();
        var holding = new Gate();
        var release = new Gate();
        var order = new List<string>();
        var holder = locks.RunExclusiveAsync("g", async () =>
        {
            holding.Open();
            await release.Task;
        }, Ct);
        await holding.Task.WaitAsync(_generous, Ct);
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var middle = locks.RunExclusiveAsync("g", () =>
        {
            order.Add("middle");
            return Task.CompletedTask;
        }, cts.Token);
        var last = locks.RunExclusiveAsync("g", () =>
        {
            order.Add("last");
            return Task.CompletedTask;
        }, Ct);

        await cts.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => middle.WaitAsync(_generous, Ct));
        release.Open();
        await holder.WaitAsync(_generous, Ct);
        await last.WaitAsync(_generous, Ct);

        Assert.Equal(["last"], order);
    }

    [Fact]
    public async Task A_later_request_cancels_a_preemptible_holder_and_then_waits_for_it_to_finish()
    {
        var locks = new GameLock();
        var holding = new Gate();
        var cancelled = new Gate();
        var events = new List<string>();
        var cancelCalls = 0;

        var holder = locks.RunExclusiveAsync("g", async handle =>
        {
            using var _ = handle.MarkPreemptible(() =>
            {
                Interlocked.Increment(ref cancelCalls);
                cancelled.Open();
            });
            holding.Open();
            await cancelled.Task;
            lock (events)
            {
                events.Add("holder finished");
            }
        }, Ct);
        await holding.Task.WaitAsync(_generous, Ct);

        await locks.RunExclusiveAsync("g", () =>
        {
            lock (events)
            {
                events.Add("request ran");
            }

            return Task.CompletedTask;
        }, Ct).WaitAsync(_generous, Ct);
        await holder.WaitAsync(_generous, Ct);

        Assert.Equal(["holder finished", "request ran"], events);
        Assert.Equal(1, cancelCalls);
    }

    [Fact]
    public async Task A_holder_that_never_marked_itself_preemptible_is_left_alone()
    {
        var locks = new GameLock();
        var holding = new Gate();
        var release = new Gate();
        var holder = locks.RunExclusiveAsync("g", async () =>
        {
            holding.Open();
            await release.Task;
        }, Ct);
        await holding.Task.WaitAsync(_generous, Ct);
        var requestRan = new Gate();

        var request = locks.RunExclusiveAsync("g", () =>
        {
            requestRan.Open();
            return Task.CompletedTask;
        }, Ct);

        await AssertStaysPending(requestRan.Task, "a plain holder must be waited for");
        release.Open();
        await holder.WaitAsync(_generous, Ct);
        await request.WaitAsync(_generous, Ct);
    }

    [Fact]
    public async Task A_holder_that_withdrew_its_registration_is_not_cancelled()
    {
        var locks = new GameLock();
        var registered = new Gate();
        var withdraw = new Gate();
        var release = new Gate();
        var cancelCalls = 0;
        var holder = locks.RunExclusiveAsync("g", async handle =>
        {
            var registration = handle.MarkPreemptible(() => Interlocked.Increment(ref cancelCalls));
            registered.Open();
            await withdraw.Task;
            registration.Dispose();
            await release.Task;
        }, Ct);
        await registered.Task.WaitAsync(_generous, Ct);
        withdraw.Open();
        await Task.Delay(_grace, Ct);

        var request = locks.RunExclusiveAsync("g", () => Task.CompletedTask, Ct);
        await Task.Delay(_grace, Ct);

        Assert.Equal(0, cancelCalls);
        release.Open();
        await holder.WaitAsync(_generous, Ct);
        await request.WaitAsync(_generous, Ct);
    }

    [Fact]
    public async Task A_holder_that_becomes_preemptible_after_a_request_is_already_waiting_is_cancelled_at_once()
    {
        var locks = new GameLock();
        var holding = new Gate();
        var mark = new Gate();
        var cancelled = new Gate();
        var cancelCalls = 0;
        var holder = locks.RunExclusiveAsync("g", async handle =>
        {
            holding.Open();
            await mark.Task;
            using var _ = handle.MarkPreemptible(() =>
            {
                Interlocked.Increment(ref cancelCalls);
                cancelled.Open();
            });
            await cancelled.Task;
        }, Ct);
        await holding.Task.WaitAsync(_generous, Ct);
        var request = locks.RunExclusiveAsync("g", () => Task.CompletedTask, Ct);

        mark.Open();

        await holder.WaitAsync(_generous, Ct);
        await request.WaitAsync(_generous, Ct);
        Assert.Equal(1, cancelCalls);
    }

    [Fact]
    public async Task Several_waiting_requests_cancel_the_holder_only_once()
    {
        var locks = new GameLock();
        var holding = new Gate();
        var cancelled = new Gate();
        var cancelCalls = 0;
        var holder = locks.RunExclusiveAsync("g", async handle =>
        {
            using var _ = handle.MarkPreemptible(() =>
            {
                Interlocked.Increment(ref cancelCalls);
                cancelled.Open();
            });
            holding.Open();
            await cancelled.Task;
        }, Ct);
        await holding.Task.WaitAsync(_generous, Ct);

        var requests = Enumerable.Range(0, 5).Select(_ => locks.RunExclusiveAsync("g", () => Task.CompletedTask, Ct)).ToList();

        await Task.WhenAll(requests).WaitAsync(_generous, Ct);
        await holder.WaitAsync(_generous, Ct);
        Assert.Equal(1, cancelCalls);
    }

    [Fact]
    public async Task Only_a_request_on_the_same_key_preempts()
    {
        var locks = new GameLock();
        var holding = new Gate();
        var release = new Gate();
        var cancelCalls = 0;
        var holder = locks.RunExclusiveAsync("one", async handle =>
        {
            using var _ = handle.MarkPreemptible(() => Interlocked.Increment(ref cancelCalls));
            holding.Open();
            await release.Task;
        }, Ct);
        await holding.Task.WaitAsync(_generous, Ct);

        await locks.RunExclusiveAsync("two", () => Task.CompletedTask, Ct).WaitAsync(_generous, Ct);

        Assert.Equal(0, cancelCalls);
        release.Open();
        await holder.WaitAsync(_generous, Ct);
    }

    [Fact]
    public async Task A_preempted_holder_that_throws_reports_to_its_own_caller_while_the_request_still_runs()
    {
        var locks = new GameLock();
        var holding = new Gate();
        var cancelled = new Gate();
        var holder = locks.RunExclusiveAsync<int>("g", async handle =>
        {
            using var _ = handle.MarkPreemptible(cancelled.Open);
            holding.Open();
            await cancelled.Task;
            throw new OperationCanceledException("preempted");
        }, Ct);
        await holding.Task.WaitAsync(_generous, Ct);

        var result = await locks.RunExclusiveAsync("g", () => Task.FromResult("request"), Ct).WaitAsync(_generous, Ct);

        Assert.Equal("request", result);
        var error = await Assert.ThrowsAsync<OperationCanceledException>(() => holder.WaitAsync(_generous, Ct));
        Assert.Equal("preempted", error.Message);
    }

    [Fact]
    public async Task A_cancel_callback_that_throws_does_not_fail_the_request()
    {
        var locks = new GameLock();
        var holding = new Gate();
        var release = new Gate();
        var holder = locks.RunExclusiveAsync("g", async handle =>
        {
            using var _ = handle.MarkPreemptible(() => throw new InvalidOperationException("callback"));
            holding.Open();
            await release.Task;
        }, Ct);
        await holding.Task.WaitAsync(_generous, Ct);

        var request = locks.RunExclusiveAsync("g", () => Task.FromResult("ok"), Ct);
        release.Open();

        Assert.Equal("ok", await request.WaitAsync(_generous, Ct));
        await holder.WaitAsync(_generous, Ct);
    }

    private static async Task AssertStaysPending(Task task, string because)
    {
        var finished = await Task.WhenAny(task, Task.Delay(_grace, Ct));
        Assert.False(ReferenceEquals(finished, task), because);
    }

    private sealed class Gate
    {
        private readonly TaskCompletionSource _source = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Task => _source.Task;

        public void Open() => _source.TrySetResult();
    }
}
