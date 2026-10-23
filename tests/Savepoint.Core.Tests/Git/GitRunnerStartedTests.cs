using Savepoint.Core.Git;

namespace Savepoint.Core.Tests.Git;

// The runner tells its caller which process it started, so a reclaim can note it for a start that follows a kill.
public sealed class GitRunnerStartedTests : IDisposable
{
    private readonly GitWorkspace _ws = new();

    public void Dispose() => _ws.Dispose();

    [Fact]
    public async Task The_runner_reports_the_process_it_started_while_it_still_runs()
    {
        await _ws.InitAsync();
        var stdin = new ReleasableStream();
        ProcessStamp? started = null;
        var call = _ws.RunAsync(new GitInvocation(["cat-file", "--batch"]) { StdinStream = stdin, Started = stamp => started = stamp });

        var until = DateTime.UtcNow.AddSeconds(10);
        while (started is null && !call.IsCompleted && DateTime.UtcNow < until)
        {
            await Task.Delay(50, TestContext.Current.CancellationToken);
        }

        try
        {
            Assert.NotNull(started);
            Assert.False(call.IsCompleted);
            Assert.True(started.Value.IsRunning());
        }
        finally
        {
            stdin.Release();
            await call;
        }

        Assert.False(started.Value.IsRunning());
    }

    [Fact]
    public async Task A_caller_that_cannot_note_the_process_gets_its_own_failure_and_git_is_stopped()
    {
        // git waits for the alias to end whatever happens to its own input, so only a stop can end it before the alias does.
        var holder = OperatingSystem.IsWindows() ? "ping -n 31 127.0.0.1" : "sleep 30";
        ProcessStamp? seen = null;
        var invocation = _ws.OutsideAnyRepository(new GitInvocation(["-c", $"alias.hold=!{holder}", "hold"])
        {
            Started = stamp =>
            {
                seen = stamp;
                throw new IOException("The note could not be written.");
            },
        });

        await Assert.ThrowsAsync<IOException>(() => _ws.Runner.RunAsync(invocation, TestContext.Current.CancellationToken));

        Assert.NotNull(seen);
        var until = DateTime.UtcNow.AddSeconds(10);
        while (seen.Value.IsRunning() && DateTime.UtcNow < until)
        {
            await Task.Delay(50, TestContext.Current.CancellationToken);
        }

        Assert.False(seen.Value.IsRunning());
    }

    [Fact]
    public async Task A_process_that_has_ended_is_not_running_whatever_took_its_number()
    {
        var ended = await LiveOrphan.EndedAsync(_ws.Runner, await InitializedGitDirAsync());

        Assert.False(ended.IsRunning());
        Assert.False((ended with { StartedAtUtcTicks = ended.StartedAtUtcTicks + TimeSpan.FromHours(1).Ticks }).IsRunning());
    }

    [Fact]
    public void This_process_is_running_and_a_stamp_with_another_start_time_is_not_it()
    {
        var self = ProcessStamp.Current();

        Assert.True(self.IsRunning());
        Assert.False((self with { StartedAtUtcTicks = self.StartedAtUtcTicks - TimeSpan.FromHours(1).Ticks }).IsRunning());
    }

    private async Task<string> InitializedGitDirAsync()
    {
        await _ws.InitAsync();
        return _ws.GitDir;
    }
}
