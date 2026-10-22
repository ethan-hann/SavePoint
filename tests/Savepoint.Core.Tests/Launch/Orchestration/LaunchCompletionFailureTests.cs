using System.Diagnostics;
using Savepoint.Core.Api;
using Savepoint.Core.Git;

namespace Savepoint.Core.Tests.Launch.Orchestration;

// An after-play save that fails or is cancelled ends the session as Completed with the reason, so the card and the job agree.
public class LaunchCompletionFailureTests
{
    private static readonly TimeSpan _held = TimeSpan.FromSeconds(20);

    [Fact(Timeout = 120_000)]
    public async Task A_save_after_play_that_fails_in_every_folder_ends_the_session_with_the_error()
    {
        var armed = false;
        using var rig = await LaunchRig.CreateAsync(
            options => options with
            {
                Engine = new GitEngineOptions
                {
                    InvocationObserver = invocation =>
                    {
                        if (Volatile.Read(ref armed) && invocation.Arguments[0] == "ls-files")
                        {
                            throw new IOException("the disk went away");
                        }
                    },
                },
            },
            gameArguments: ["--exit-after", "800"]);
        var vanilla = await rig.ProfileAsync("Vanilla");
        await rig.StartAsync(new LaunchTarget.ProfileTip(vanilla.Id));
        await rig.WaitForAsync<SessionState.Exited>();
        Volatile.Write(ref armed, true);

        Assert.True((await rig.Launch.CompleteSessionAsync(rig.Game.Id, new SessionCompletion.CaptureNew("After play"))).Ok);
        var done = Assert.IsType<SessionState.Completed>((await rig.WaitForAsync<SessionState.Completed>()).State);
        Volatile.Write(ref armed, false);

        Assert.NotNull(done.Error);
        Assert.Equal("error.unknown", done.Error.MessageId);
        Assert.All(Assert.IsType<CaptureResult>(done.Result).Folders, folder => Assert.False(folder.Ok));
    }

    [Fact(Timeout = 120_000)]
    public async Task A_save_after_play_that_is_cancelled_ends_the_session_with_the_cancelled_error()
    {
        using var rig = await LaunchRig.CreateAsync(gameArguments: ["--exit-after", "800"]);
        var vanilla = await rig.ProfileAsync("Vanilla");
        await rig.StartAsync(new LaunchTarget.ProfileTip(vanilla.Id));
        await rig.WaitForAsync<SessionState.Exited>();
        using var reached = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        rig.Workspace.Disk.FreePerPath = path =>
        {
            if (path.StartsWith(rig.Workspace.DefaultStorageRoot, StringComparison.OrdinalIgnoreCase)
                && new StackTrace().ToString().Contains("CaptureCoreAsync", StringComparison.Ordinal))
            {
                reached.Set();
                release.Wait(_held);
            }

            return null;
        };
        try
        {
            Assert.True((await rig.Launch.CompleteSessionAsync(rig.Game.Id, new SessionCompletion.CaptureNew("After play"))).Ok);
            Assert.True(reached.Wait(_held, rig.Token), "the save never reached the engine");
            var completing = Assert.IsType<SessionState.Completing>(rig.Launch.GetSession(rig.Game.Id)!.State);

            await rig.Jobs.CancelAsync(completing.JobId);
            release.Set();
            var done = Assert.IsType<SessionState.Completed>((await rig.WaitForAsync<SessionState.Completed>()).State);

            Assert.Equal("error.capture.cancelled", done.Error?.MessageId);
        }
        finally
        {
            release.Set();
        }
    }
}
