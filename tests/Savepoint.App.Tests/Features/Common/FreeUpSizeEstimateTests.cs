using Savepoint.App.Features.Common;
using Savepoint.App.Tests.Fakes;
using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;

namespace Savepoint.App.Tests.Features.Common;

// Both Free up space screens ask for the estimate the same way: in the background, once for each preview, dropped when a newer
// preview replaces it, and left out of the words when it cannot be worked out.
public class FreeUpSizeEstimateTests
{
    private static readonly TimeSpan _grace = TimeSpan.FromMilliseconds(100);

    private readonly FreeUpSizeEstimate _estimate = new(new FixedTimeProvider(DateTimeOffset.UnixEpoch), _grace);
    private readonly List<CancellationToken> _asked = [];

    private static FreeUpSpaceToken TokenOf(string value) => new([new FreeUpSpaceRecord("game-1", RepoRole.Install, "refs/savepoint/removed/" + value, value)]);

    private Func<FreeUpSpaceToken, CancellationToken, Task<OperationResult<long>>> Answering(long bytes) => (_, ct) =>
    {
        _asked.Add(ct);
        return Task.FromResult(OperationResult.Success(bytes));
    };

    [Fact]
    public async Task A_size_the_backend_works_out_is_given_back()
    {
        var size = await _estimate.Start(TokenOf("a"), Answering(1_200_000_000));

        Assert.Equal(1_200_000_000L, size);
    }

    [Fact]
    public async Task Nothing_to_say_is_given_back_for_zero_bytes_and_for_a_failure()
    {
        var zero = await _estimate.Start(TokenOf("a"), Answering(0));
        var failed = await _estimate.Start(TokenOf("b"), (_, _) => Task.FromResult(OperationResult.Failure<long>(ErrorInfo.From(new InvalidOperationException("Busy.")))));

        Assert.Null(zero);
        Assert.Null(failed);
    }

    [Fact]
    public async Task Asking_again_for_the_same_preview_while_it_is_running_shares_the_question()
    {
        var hold = new TaskCompletionSource<OperationResult<long>>();
        var first = _estimate.Start(TokenOf("a"), (_, _) =>
        {
            _asked.Add(CancellationToken.None);
            return hold.Task;
        });
        var second = _estimate.Start(TokenOf("a"), Answering(9_000_000));
        hold.SetResult(OperationResult.Success(5_000_000L));

        Assert.Same(first, second);
        Assert.Equal(5_000_000L, await second);
        Assert.Single(_asked);
    }

    [Fact]
    public async Task Asking_again_once_it_has_answered_asks_the_backend_again_because_the_store_may_have_changed()
    {
        var first = await _estimate.Start(TokenOf("a"), Answering(5_000_000));
        var second = await _estimate.Start(TokenOf("a"), Answering(2_000_000));

        Assert.Equal(5_000_000L, first);
        Assert.Equal(2_000_000L, second);
    }

    [Fact]
    public async Task A_newer_preview_cancels_the_estimate_still_running_for_the_older_one()
    {
        var hold = new TaskCompletionSource<OperationResult<long>>();
        var older = _estimate.Start(TokenOf("a"), (_, ct) =>
        {
            _asked.Add(ct);
            return hold.Task.WaitAsync(ct);
        });

        var newer = _estimate.Start(TokenOf("b"), Answering(7_000_000));

        Assert.True(_asked[0].IsCancellationRequested);
        Assert.Null(await older);
        Assert.Equal(7_000_000L, await newer);
    }

    [Fact]
    public async Task Waiting_gives_the_size_when_it_arrives_in_time_and_nothing_when_it_does_not()
    {
        var hold = new TaskCompletionSource<OperationResult<long>>();
        var slow = _estimate.Start(TokenOf("a"), (_, _) => hold.Task);
        var fast = _estimate.Start(TokenOf("b"), Answering(3_000_000));

        var late = await _estimate.WaitAsync(slow);
        var onTime = await _estimate.WaitAsync(fast);
        hold.SetResult(OperationResult.Success(1L));

        Assert.Null(late);
        Assert.Equal(3_000_000L, onTime);
    }

    [Fact]
    public async Task Asking_cancelling_or_disposing_after_dispose_does_nothing_and_asks_nobody()
    {
        _estimate.Dispose();

        var asked = _estimate.Start(TokenOf("a"), Answering(5_000_000));
        _estimate.Cancel();
        _estimate.Dispose();

        Assert.Null(await asked);
        Assert.Empty(_asked);
    }

    [Fact]
    public async Task Cancelling_stops_the_estimate_that_is_running()
    {
        var hold = new TaskCompletionSource<OperationResult<long>>();
        var running = _estimate.Start(TokenOf("a"), (_, ct) =>
        {
            _asked.Add(ct);
            return hold.Task.WaitAsync(ct);
        });

        _estimate.Cancel();

        Assert.True(_asked[0].IsCancellationRequested);
        Assert.Null(await running);
    }
}
