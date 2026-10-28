using Savepoint.Core.Backend;

namespace Savepoint.Core.Tests.Backend;

public class StorageGateTests
{
    // A holder that should stay out would get in within moments, and only waiting shows that it does not. The same pause lets a queued holder reach the gate.
    private static readonly TimeSpan _settle = TimeSpan.FromMilliseconds(150);

    [Fact]
    public async Task Shared_holders_run_side_by_side()
    {
        var gate = new StorageGate();

        using var first = await gate.EnterSharedAsync(TestContext.Current.CancellationToken);

        // A second shared holder that had to wait for the first would never get in while the first is held; the deadline turns that into a failure.
        var second = gate.EnterSharedAsync(TestContext.Current.CancellationToken);
        using var lease = await second.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        Assert.True(second.IsCompletedSuccessfully, "the second shared holder did not get in beside the first");
    }

    [Fact]
    public async Task An_exclusive_holder_waits_for_every_shared_holder()
    {
        var gate = new StorageGate();
        var shared = await gate.EnterSharedAsync(TestContext.Current.CancellationToken);

        var exclusive = gate.EnterExclusiveAsync(TestContext.Current.CancellationToken);

        await Task.Delay(_settle, TestContext.Current.CancellationToken);
        Assert.False(exclusive.IsCompleted);
        shared.Dispose();
        using var lease = await exclusive.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task A_waiting_exclusive_holder_keeps_new_shared_holders_out()
    {
        var gate = new StorageGate();
        var shared = await gate.EnterSharedAsync(TestContext.Current.CancellationToken);
        var exclusive = gate.EnterExclusiveAsync(TestContext.Current.CancellationToken);
        await Task.Delay(_settle, TestContext.Current.CancellationToken);

        var late = gate.EnterSharedAsync(TestContext.Current.CancellationToken);

        await Task.Delay(_settle, TestContext.Current.CancellationToken);
        Assert.False(late.IsCompleted);
        shared.Dispose();
        (await exclusive.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken)).Dispose();
        (await late.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken)).Dispose();
    }

    [Fact]
    public async Task Shared_holders_wait_while_an_exclusive_holder_is_inside()
    {
        var gate = new StorageGate();
        var exclusive = await gate.EnterExclusiveAsync(TestContext.Current.CancellationToken);

        var shared = gate.EnterSharedAsync(TestContext.Current.CancellationToken);

        await Task.Delay(_settle, TestContext.Current.CancellationToken);
        Assert.False(shared.IsCompleted);
        exclusive.Dispose();
        (await shared.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken)).Dispose();
    }

    [Fact]
    public async Task A_cancelled_wait_for_the_exclusive_side_lets_shared_holders_through_again()
    {
        var gate = new StorageGate();
        var shared = await gate.EnterSharedAsync(TestContext.Current.CancellationToken);
        using var giveUp = new CancellationTokenSource();
        var exclusive = gate.EnterExclusiveAsync(giveUp.Token);
        await Task.Delay(_settle, TestContext.Current.CancellationToken);

        await giveUp.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => exclusive);

        using var another = await gate.EnterSharedAsync(TestContext.Current.CancellationToken).WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        shared.Dispose();
    }

    [Fact]
    public async Task Disposing_a_lease_twice_releases_it_once()
    {
        var gate = new StorageGate();
        var first = await gate.EnterSharedAsync(TestContext.Current.CancellationToken);
        var second = await gate.EnterSharedAsync(TestContext.Current.CancellationToken);

        first.Dispose();
        first.Dispose();
        var exclusive = gate.EnterExclusiveAsync(TestContext.Current.CancellationToken);

        await Task.Delay(_settle, TestContext.Current.CancellationToken);
        Assert.False(exclusive.IsCompleted);
        second.Dispose();
        (await exclusive.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken)).Dispose();
    }
}
