using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Savepoint.Core.Git;

namespace Savepoint.Core.Tests.Git;

public sealed class GitCancellationTests : IDisposable
{
    private static readonly TimeSpan _generous = TimeSpan.FromSeconds(30);

    private readonly GitWorkspace _ws = new();

    public void Dispose() => _ws.Dispose();

    [Fact]
    public async Task A_token_that_is_already_cancelled_starts_nothing()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var thrown = await Assert.ThrowsAsync<OperationCanceledException>(
            () => _ws.Runner.RunAsync(new GitInvocation(["--version"]), cts.Token));

        Assert.Equal(cts.Token, thrown.CancellationToken);
    }

    [Fact]
    public async Task Cancelling_returns_promptly_while_git_waits_on_an_open_stdin()
    {
        await _ws.InitAsync();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var invocation = new GitInvocation(["cat-file", "--batch"]) { GitDir = _ws.GitDir, WorkTree = _ws.WorkTree, StdinStream = new NeverEndingStream() };
        var call = _ws.Runner.RunAsync(invocation, cts.Token);
        await Task.Delay(500, TestContext.Current.CancellationToken);
        Assert.False(call.IsCompleted, "git should still be waiting for input");

        var clock = Stopwatch.StartNew();
        await cts.CancelAsync();
        var thrown = await Assert.ThrowsAsync<OperationCanceledException>(() => call.WaitAsync(_generous, TestContext.Current.CancellationToken));

        Assert.Equal(cts.Token, thrown.CancellationToken);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(10), $"cancelling took {clock.Elapsed}");
    }

    [Fact]
    public async Task Cancelling_kills_every_process_in_the_tree_not_just_the_one_that_was_started()
    {
        // The alias makes a second git run under a shell (on Windows cmd\git.exe, git.exe, sh.exe, git.exe). The
        // listener sees the connection close only if that innermost git is gone, and git:// needs no remote helper.
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        var accepted = listener.AcceptTcpClientAsync(TestContext.Current.CancellationToken).AsTask();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var invocation = _ws.OutsideAnyRepository(new GitInvocation(["-c", $"alias.probe=!git ls-remote git://127.0.0.1:{port}/repo.git", "probe"]));
        var call = _ws.Runner.RunAsync(invocation, cts.Token);

        await Task.WhenAny(accepted, Task.Delay(_generous, TestContext.Current.CancellationToken));
        Assert.True(accepted.IsCompletedSuccessfully, "git never connected to the listener");
        using var client = await accepted;
        var stream = client.GetStream();
        await stream.ReadAtLeastAsync(new byte[1], 1, cancellationToken: TestContext.Current.CancellationToken);
        await cts.CancelAsync();
        await Assert.ThrowsAsync<OperationCanceledException>(() => call.WaitAsync(_generous, TestContext.Current.CancellationToken));

        Assert.True(await ConnectionClosesAsync(stream, TimeSpan.FromSeconds(15)), "the git that held the connection is still alive");
    }

    [Fact]
    public async Task A_leftover_process_holding_the_output_pipe_cannot_hang_the_call()
    {
        // The alias leaves a background process that inherits git's stdout and outlives git itself.
        // The holder outlives the limit below by a wide margin, so a loaded machine cannot blur "returned early" with "waited".
        var holder = OperatingSystem.IsWindows() ? "ping -n 31 127.0.0.1 &" : "sleep 30 &";
        using var impatient = new GitWorkspace(new GitRunnerOptions { DrainTimeout = TimeSpan.FromMilliseconds(500) });
        var invocation = new GitInvocation(["-c", $"alias.leave=!{holder}", "leave"]);

        var clock = Stopwatch.StartNew();
        var result = await impatient.RunPlainAsync(invocation);

        Assert.Equal(0, result.ExitCode);
        Assert.True(clock.Elapsed < TimeSpan.FromSeconds(15), $"the call waited {clock.Elapsed} for the leftover process");
    }

    [Fact]
    public async Task A_callback_that_throws_stops_a_git_that_is_still_running()
    {
        await _ws.InitAsync();
        const string emptyTree = "4b825dc642cb6eb9a060e54bf8d69288fbee4904\n";
        var invocation = new GitInvocation(["cat-file", "--batch-check"])
        {
            GitDir = _ws.GitDir,
            WorkTree = _ws.WorkTree,
            StdinStream = new OneChunkThenHangStream(System.Text.Encoding.ASCII.GetBytes(emptyTree)),
            OnStdoutLine = _ => throw new InvalidOperationException("from the callback"),
        };

        var thrown = await Assert.ThrowsAsync<InvalidOperationException>(
            () => _ws.Runner.RunAsync(invocation, TestContext.Current.CancellationToken).WaitAsync(_generous, TestContext.Current.CancellationToken));

        Assert.Equal("from the callback", thrown.Message);
    }
    [Fact]
    public async Task Output_from_a_leftover_process_is_not_delivered_after_the_call_has_returned()
    {
        // The leftover keeps printing for a few seconds after git itself exited.
        var holder = OperatingSystem.IsWindows() ? "ping -n 6 127.0.0.1 &" : "for i in 1 2 3 4 5 6; do echo tick; sleep 1; done &";
        using var impatient = new GitWorkspace(new GitRunnerOptions { DrainTimeout = TimeSpan.FromMilliseconds(500) });
        var delivered = 0;
        var invocation = new GitInvocation(["-c", $"alias.leave=!{holder}", "leave"])
        {
            OnStdoutLine = _ => Interlocked.Increment(ref delivered),
        };

        await impatient.RunPlainAsync(invocation);
        var atReturn = Volatile.Read(ref delivered);
        await Task.Delay(TimeSpan.FromSeconds(2.5), TestContext.Current.CancellationToken);

        Assert.Equal(atReturn, Volatile.Read(ref delivered));
    }
    [Fact]
    public async Task No_callback_is_still_running_when_the_call_returns()
    {
        using var release = new ManualResetEventSlim();
        var running = 0;
        using var impatient = new GitWorkspace(new GitRunnerOptions { DrainTimeout = TimeSpan.FromMilliseconds(200) });
        var invocation = new GitInvocation(["--version"])
        {
            OnStdoutLine = _ =>
            {
                Interlocked.Increment(ref running);
                release.Wait(TestContext.Current.CancellationToken);
                Interlocked.Decrement(ref running);
            },
        };
        var releaseLater = Task.Delay(700, TestContext.Current.CancellationToken).ContinueWith(_ => release.Set(), TaskScheduler.Default);

        await impatient.RunPlainAsync(invocation);

        Assert.Equal(0, Volatile.Read(ref running));
        await releaseLater;
    }
    private static async Task<bool> ConnectionClosesAsync(NetworkStream stream, TimeSpan timeout)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        cts.CancelAfter(timeout);
        var buffer = new byte[4096];
        try
        {
            while (true)
            {
                if (await stream.ReadAsync(buffer, cts.Token) == 0)
                {
                    return true;
                }
            }
        }
        catch (IOException)
        {
            return true; // reset by the peer's death
        }
        catch (OperationCanceledException) when (!TestContext.Current.CancellationToken.IsCancellationRequested)
        {
            return false;
        }
    }

    private sealed class NeverEndingStream : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class OneChunkThenHangStream(byte[] chunk) : Stream
    {
        private bool _sent;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (!_sent)
            {
                _sent = true;
                chunk.CopyTo(buffer);
                return chunk.Length;
            }

            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }

        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
