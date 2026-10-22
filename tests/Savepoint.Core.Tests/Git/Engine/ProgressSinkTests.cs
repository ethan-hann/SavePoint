namespace Savepoint.Core.Tests.Git.Engine;

// Progress only decorates a long operation: a sink that fails must not stop git half way through a switch or a capture.
[Collection(EngineCollections.A)]
public sealed class ProgressSinkTests : IDisposable
{
    private readonly EngineWorkspace _ws = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public void Dispose() => _ws.Dispose();

    private static ProgressLog FailingSink() => new() { OnReport = _ => throw new InvalidOperationException("the screen went away") };

    [Fact]
    public async Task A_capture_finishes_when_the_progress_sink_throws()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.WriteMany(repo, "Data", 30);
        var sink = FailingSink();

        var error = await Record.ExceptionAsync(() => _ws.CaptureAsync(repo, "profile-1", progress: sink));

        Assert.Null(error);
        Assert.NotEmpty(sink.Reports);
        Assert.Equal(30, (await _ws.TreeAsync(repo, "profile-1")).Length);
    }

    [Fact]
    public async Task A_switch_with_a_sink_does_not_wait_for_gits_own_two_second_delay_before_showing_its_meter()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.WriteMany(repo, "Data", 5, content: "a");
        await _ws.CaptureAsync(repo, "profile-a");
        await _ws.GitWriteAsync(repo, "checkout", "--quiet", "-b", "profile-b");
        _ws.WriteMany(repo, "Data", 5, content: "b");
        await _ws.CaptureAsync(repo, "profile-b");

        await _ws.Engine.CheckoutBranchAsync(repo, "profile-a", new ProgressLog(), Ct);

        var checkout = Assert.Single(_ws.Invocations, invocation => invocation.Arguments[0] == "checkout" && invocation.Arguments.Contains("--progress"));
        Assert.Equal("0", checkout.ExtraEnvironment?["GIT_PROGRESS_DELAY"]);
    }

    [Fact]
    public async Task A_switch_finishes_when_the_progress_sink_throws()
    {
        var repo = await _ws.InitializedRepoAsync();
        _ws.WriteMany(repo, "Data", 400, content: "a");
        await _ws.CaptureAsync(repo, "profile-a");
        var a = TreeSnapshot.Take(repo.WorkTree);
        await _ws.GitWriteAsync(repo, "checkout", "--quiet", "-b", "profile-b");
        _ws.WriteMany(repo, "Data", 400, content: "b");
        await _ws.CaptureAsync(repo, "profile-b");
        var sink = FailingSink();

        var error = await Record.ExceptionAsync(() => _ws.Engine.CheckoutBranchAsync(repo, "profile-a", sink, Ct));

        Assert.Null(error);
        Assert.NotEmpty(sink.Reports);
        Assert.Equal(a, TreeSnapshot.Take(repo.WorkTree));
        Assert.Null((await _ws.Engine.GetStatusAsync(repo, Ct)).InterruptedSwitch);
    }
}
