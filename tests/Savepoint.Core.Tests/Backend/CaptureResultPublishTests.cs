using Savepoint.Core.Api;

namespace Savepoint.Core.Tests.Backend;

// A save that has written its points is over: it is not held up by work that queued on the same game after it (D31).
public class CaptureResultPublishTests
{
    private static readonly TimeSpan _held = TimeSpan.FromSeconds(20);

    // The optimization has to join the game's queue behind the capture. The queue shows no sign of a waiter, so a pause is the only way to order the two.
    private static readonly TimeSpan _queueSettle = TimeSpan.FromMilliseconds(300);

    [Fact(Timeout = 120_000)]
    public async Task A_capture_ends_as_soon_as_its_points_are_written_even_while_an_optimization_waits_on_the_same_game()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync();
        var (profile, _) = await workspace.CreateProfileAsync(game);
        await workspace.WaitForIdleAsync(TimeSpan.FromSeconds(60));
        using var reachedCapture = new ManualResetEventSlim();
        using var releaseCapture = new ManualResetEventSlim();
        using var reachedOptimization = new ManualResetEventSlim();
        using var releaseOptimization = new ManualResetEventSlim();

        // The storage is asked for free space before each write. The capture's questions hold the capture, the optimization's hold the optimization.
        workspace.Disk.FreePerPath = path =>
        {
            if (!path.StartsWith(workspace.DefaultStorageRoot, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (IsAskedByTheCapture())
            {
                reachedCapture.Set();
                releaseCapture.Wait(_held);
            }
            else
            {
                reachedOptimization.Set();
                releaseOptimization.Wait(_held);
            }

            return null;
        };
        Guid optimization;
        JobSnapshot? capture = null;
        try
        {
            BackendWorkspace.WriteFile(Path.Combine(game.External!.Path, "slot2.sav"), "a second save");
            var started = await workspace.Profiles.CaptureAsync(game.Id, profile.Id, "Second save");
            Assert.True(started.Ok, started.Error?.Detail);
            Assert.True(reachedCapture.Wait(_held, workspace.Token), "the capture never reached the engine");
            var queued = await workspace.Games.OptimizeStorageAsync(game.Id);
            Assert.True(queued.Ok, queued.Error?.Detail);
            optimization = queued.Value.JobId;
            await Task.Delay(_queueSettle, workspace.Token);

            releaseCapture.Set();
            Assert.True(reachedOptimization.Wait(_held, workspace.Token), "the optimization never got the game");
            capture = await WaitUntilOverAsync(workspace, started.Value.JobId);
        }
        finally
        {
            releaseCapture.Set();
            releaseOptimization.Set();
        }

        Assert.NotNull(capture);
        Assert.Equal(JobStatus.Succeeded, capture.Status);
        Assert.Equal(JobStatus.Running, workspace.Jobs.Get(optimization)?.Status);
        await workspace.WaitForIdleAsync(TimeSpan.FromSeconds(60));
    }

    // The capture asks again, from its staging step, after an await. By then the stack no longer holds the method that started it,
    // so every engine method of the capture counts, or the later question would be taken for the optimization's. The folders' joint
    // room check is asked from the gate that the capture's folders report to, which is also the capture's.
    private static bool IsAskedByTheCapture()
    {
        var stack = new System.Diagnostics.StackTrace().ToString();
        return stack.Contains("GitEngine.Capture", StringComparison.Ordinal) || stack.Contains("GitEngine.StageAsync", StringComparison.Ordinal) || stack.Contains("CaptureRoomGate", StringComparison.Ordinal);
    }

    // Null as a job when it is still running after a generous wait, which fails the assertion that follows with a clear message.
    private static async Task<JobSnapshot?> WaitUntilOverAsync(BackendWorkspace workspace, Guid jobId)
    {
        var until = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (DateTime.UtcNow < until)
        {
            if (workspace.Jobs.Get(jobId) is { Status: not JobStatus.Running } over)
            {
                return over;
            }

            await Task.Delay(25, workspace.Token);
        }

        Assert.Fail("the capture was still running ten seconds after its points were written");
        return null;
    }
}
