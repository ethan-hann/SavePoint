using Savepoint.Core.Git;

namespace Savepoint.Core.Tests.Git;

// A real git process that keeps running until the test ends it, standing in for one a killed SavePoint left behind.
internal sealed class LiveOrphan
{
    private readonly ReleasableStream _stdin;
    private readonly Task _call;

    private LiveOrphan(ProcessStamp stamp, ReleasableStream stdin, Task call)
    {
        Stamp = stamp;
        _stdin = stdin;
        _call = call;
    }

    public ProcessStamp Stamp { get; }

    public static async Task<LiveOrphan> StartAsync(GitRunner runner, string gitDir)
    {
        var stdin = new ReleasableStream();
        var started = new TaskCompletionSource<ProcessStamp>(TaskCreationOptions.RunContinuationsAsynchronously);
        var call = runner.RunAsync(new GitInvocation(["cat-file", "--batch"]) { GitDir = gitDir, StdinStream = stdin, Started = stamp => started.TrySetResult(stamp) }, TestContext.Current.CancellationToken);
        var first = await Task.WhenAny(started.Task, call, Task.Delay(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken));
        if (!ReferenceEquals(first, started.Task))
        {
            stdin.Release();
            await call;
            Assert.Fail("The runner never reported the git process it started.");
        }

        return new LiveOrphan(await started.Task, stdin, call);
    }

    // A process that has already ended, as the stamp of a SavePoint or a git that is gone.
    public static async Task<ProcessStamp> EndedAsync(GitRunner runner, string gitDir)
    {
        var orphan = await StartAsync(runner, gitDir);
        await orphan.EndAsync();
        return orphan.Stamp;
    }

    public async Task EndAsync()
    {
        _stdin.Release();
        await _call;
    }
}
