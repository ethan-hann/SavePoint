using Savepoint.Core.Git;

namespace Savepoint.Core.Tests.Git;

public sealed class GitOutputSinkTests
{
    // A detach that did not wait for the running callback would return within moments, and only waiting shows that it does not.
    private static readonly TimeSpan _detachWindow = TimeSpan.FromMilliseconds(300);

    [Fact]
    public void Everything_is_kept_while_it_fits_the_limit()
    {
        var sink = NewSink(collect: true, limit: 100);

        sink.Append("first ");
        sink.Append("second");
        sink.Complete();

        Assert.Equal("first second", sink.Text);
    }

    [Fact]
    public void Only_the_end_is_kept_once_the_limit_is_exceeded()
    {
        var sink = NewSink(collect: true, limit: 10);

        for (var i = 0; i < 50; i++)
        {
            sink.Append("0123456789");
            sink.Append($"[{i}]");
        }

        sink.Complete();

        Assert.Equal("0123456789[49]"[^10..], sink.Text);
    }

    [Fact]
    public void Nothing_is_kept_when_collecting_is_off()
    {
        var sink = NewSink(collect: false, limit: 100);

        sink.Append("ignored");
        sink.Complete();

        Assert.Equal(string.Empty, sink.Text);
    }

    [Fact]
    public void Lines_go_to_the_callback_whether_or_not_text_is_kept()
    {
        var lines = new List<string>();
        var sink = NewSink(collect: true, limit: 100, onLine: lines.Add);

        sink.Append("a\r");
        sink.Append("b\nc");
        sink.Complete();

        Assert.Equal<IEnumerable<string>>(new[] { "a", "b", "c" }, lines);
        Assert.Equal("a\rb\nc", sink.Text);
    }

    [Fact]
    public void A_callback_that_throws_is_recorded_once_and_not_called_again()
    {
        var calls = 0;
        var notified = 0;
        var failure = new CallbackFailure();
        var sink = new GitOutputSink(collect: false, 100, _ => { calls++; throw new InvalidOperationException("boom"); }, failure, () => notified++);

        sink.Append("one\ntwo\nthree\n");
        sink.Complete();

        Assert.Equal(1, calls);
        Assert.Equal(1, notified);
        Assert.True(failure.HasFailed);
        Assert.Throws<InvalidOperationException>(failure.ThrowIfFailed);
    }

    [Fact]
    public void Text_and_lines_that_arrive_after_detaching_are_ignored()
    {
        var lines = new List<string>();
        var sink = NewSink(collect: true, limit: 100, onLine: lines.Add);
        sink.Append("kept\n");

        sink.Detach();
        sink.Append("late\n");
        sink.Complete();

        Assert.Equal("kept\n", sink.Text);
        Assert.Equal<IEnumerable<string>>(new[] { "kept" }, lines);
    }

    [Fact]
    public async Task Detaching_waits_for_a_callback_that_is_still_running()
    {
        using var entered = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var finished = false;
        var sink = NewSink(collect: false, limit: 100, onLine: _ =>
        {
            entered.Set();
            release.Wait(TestContext.Current.CancellationToken);
            finished = true;
        });
        var append = Task.Run(() => sink.Append("line\n"), TestContext.Current.CancellationToken);
        entered.Wait(TestContext.Current.CancellationToken);

        var detach = Task.Run(sink.Detach, TestContext.Current.CancellationToken);
        await Task.Delay(_detachWindow, TestContext.Current.CancellationToken);
        Assert.False(detach.IsCompleted, "Detach returned while a callback was still running");
        release.Set();

        await detach.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.True(finished);
        await append;
    }
    [Fact]
    public void Bytes_are_kept_until_the_sink_is_detached()
    {
        var sink = new GitByteSink();

        sink.Append([1, 2, 3]);
        sink.Append([4]);
        sink.Detach();
        sink.Append([5, 6]);

        Assert.Equal(new byte[] { 1, 2, 3, 4 }, sink.ToArray());
    }
    private static GitOutputSink NewSink(bool collect, int limit, Action<string>? onLine = null) =>
        new(collect, limit, onLine, new CallbackFailure(), () => { });
}
