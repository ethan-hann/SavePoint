namespace Savepoint.Core.Tests.Backend;

// A world is built once by the first test that asks and shared with the tests that ask while it builds. These tests join a build that is
// still running and then end it, the way a test that was cancelled in the middle of a build does.
public sealed class BackendWorkspaceWorldTests
{
    private static string UniqueName() => "world-" + Guid.NewGuid().ToString("N");

    [Fact]
    public async Task A_test_that_joined_a_build_its_starter_lost_to_a_cancellation_builds_the_world_itself_whatever_the_failure_looks_like()
    {
        var name = UniqueName();
        using var starterCancellation = new CancellationTokenSource();
        var release = new TaskCompletionSource();
        var starter = BackendWorkspace.FromWorldAsync(name, async _ => await release.Task, testCancellation: starterCancellation.Token);
        var joinedBuilds = 0;
        var joiner = BackendWorkspace.FromWorldAsync(name, _ =>
        {
            Interlocked.Increment(ref joinedBuilds);
            return Task.CompletedTask;
        });

        // A cancelled capture inside the build surfaces as a failed assertion, not as an OperationCanceledException.
        await starterCancellation.CancelAsync();
        release.SetException(new Xunit.Sdk.XunitException("the capture was canceled"));

        await Assert.ThrowsAsync<Xunit.Sdk.XunitException>(() => starter);
        using var workspace = await joiner;
        Assert.Equal(1, joinedBuilds);
    }

    [Fact]
    public async Task A_test_that_joined_a_build_that_failed_for_its_own_reason_gets_that_failure_and_builds_nothing()
    {
        var name = UniqueName();
        var release = new TaskCompletionSource();
        var starter = BackendWorkspace.FromWorldAsync(name, async _ => await release.Task);
        var joinedBuilds = 0;
        var joiner = BackendWorkspace.FromWorldAsync(name, _ =>
        {
            Interlocked.Increment(ref joinedBuilds);
            return Task.CompletedTask;
        });

        release.SetException(new Xunit.Sdk.XunitException("the world is wrong"));

        var failure = await Assert.ThrowsAsync<Xunit.Sdk.XunitException>(() => joiner);
        Assert.Equal("the world is wrong", failure.Message);
        await Assert.ThrowsAsync<Xunit.Sdk.XunitException>(() => starter);
        Assert.Equal(0, joinedBuilds);
    }
}
