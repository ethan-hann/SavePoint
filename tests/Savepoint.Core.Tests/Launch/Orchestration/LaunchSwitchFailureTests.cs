using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Git;
using Savepoint.Core.Services.Storage;
using Savepoint.Core.Tests.Services.Exe;

namespace Savepoint.Core.Tests.Launch.Orchestration;

// A switch that is stopped, held up or refused part way: the error is specific, the folder says it is unfinished, and a repeat finishes it.
public class LaunchSwitchFailureTests
{
    private static readonly TimeSpan _held = TimeSpan.FromSeconds(20);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static LaunchTarget Tip(Profile profile) => new LaunchTarget.ProfileTip(profile.Id);

    [Fact(Timeout = 150_000)]
    public async Task A_file_the_game_holds_open_fails_the_switch_by_name_and_a_retry_after_it_is_let_go_finishes()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "only Windows refuses to replace a file another program holds open");
        var (rig, vanilla, _) = await LaunchRig.WithTwoProfilesAsync();
        using var _ = rig;
        var held = Path.Combine(rig.InstallPath, "data", "base.pak");
        var ready = Path.Combine(rig.Workspace.Root, "locker-ready.txt");
        var locker = FakeGameProcess.Start("--lock", held, "--ready-file", ready);
        try
        {
            await FakeGameProcess.WaitForReadyFileAsync(ready);

            var failed = await rig.StartAsync(Tip(vanilla));

            Assert.Equal(JobStatus.Failed, failed.Status);
            Assert.Equal("error.gitOperation.checkoutFailed", failed.Error?.MessageId);
            Assert.EndsWith("base.pak", (string)failed.Error!.Args["path"]!, StringComparison.Ordinal);
            await rig.WaitForEventAsync(session => session.State is SessionState.Failed failure && failure.Error == failed.Error, "Failed with the job's error");
        }
        finally
        {
            locker.Dispose();
        }

        var retried = await rig.StartAsync(Tip(vanilla));

        Assert.Equal(JobStatus.Succeeded, retried.Status);
        Assert.Equal("base content", rig.ReadInstall("data/base.pak"));
        Assert.False(rig.InstallHas("mods/a.txt"));
        await rig.WaitForGameAsync();
        var status = await rig.StatusAsync(RepoRole.Install);
        Assert.True(status.IsClean);
        Assert.Null(status.InterruptedSwitch);
    }

    [Fact(Timeout = 150_000)]
    public async Task Unsaved_work_beside_a_file_the_game_holds_open_still_stops_the_launch_as_unsaved_changes()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "only Windows refuses to read a file another program holds open");
        var (rig, vanilla, _) = await LaunchRig.WithTwoProfilesAsync();
        using var _ = rig;
        rig.WriteInstall("mods/unsaved.txt", "work nobody captured");
        var ready = Path.Combine(rig.Workspace.Root, "locker-ready.txt");
        using var locker = FakeGameProcess.Start("--lock", Path.Combine(rig.InstallPath, "data", "base.pak"), "--ready-file", ready);
        await FakeGameProcess.WaitForReadyFileAsync(ready);

        var failed = await rig.StartAsync(Tip(vanilla));

        Assert.Equal(JobStatus.Failed, failed.Status);
        Assert.Equal("error.launch.dirtyWorkingTree", failed.Error?.MessageId);
        Assert.True(rig.InstallHas("mods/unsaved.txt"));
    }

    [Fact(Timeout = 150_000)]
    public async Task Cancelling_the_switch_ends_the_launch_cancelled_and_leaves_a_state_a_second_launch_finishes_from()
    {
        var (rig, vanilla, _) = await LaunchRig.WithTwoProfilesAsync();
        using var _ = rig;
        using var reached = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();

        // The engine asks for the install folder's free space just before it starts the switch; holding that answer holds the switch.
        // The first question is the check made before any folder is switched, so the second is the one held.
        var installQuestions = 0;
        rig.Workspace.Disk.FreePerPath = path =>
        {
            if (path.StartsWith(rig.InstallPath, StringComparison.OrdinalIgnoreCase) && Interlocked.Increment(ref installQuestions) == 2 && !release.IsSet)
            {
                reached.Set();
                release.Wait(_held);
            }

            return null;
        };
        JobSnapshot cancelled;
        try
        {
            var started = await rig.Launch.StartAsync(rig.Game.Id, Tip(vanilla));
            Assert.True(started.Ok, started.Error?.Detail);
            Assert.True(reached.Wait(_held, Ct), "the switch never reached the engine");
            await rig.Jobs.CancelAsync(started.Value.JobId);
            release.Set();
            cancelled = await rig.WaitForJobAsync(started.Value.JobId);
        }
        finally
        {
            release.Set();
        }

        Assert.Equal(JobStatus.Cancelled, cancelled.Status);
        Assert.Equal("error.launch.canceled", cancelled.Error?.MessageId);
        await rig.WaitForEventAsync(session => session.State is SessionState.Failed failure && failure.Error == cancelled.Error, "Failed with the job's error");
        Assert.Empty(rig.GameProcessIds());
        Assert.False(rig.Workspace.Backend.Services.Sessions.IsGameRunning(rig.Game.Id));

        // The folder says it was being switched, and the preview treats that as work to finish, not as unsaved changes.
        Assert.NotNull((await rig.StatusAsync(RepoRole.Install)).InterruptedSwitch);
        var preview = await rig.PreviewAsync(Tip(vanilla));
        Assert.False(preview.Install.Dirty);
        Assert.False(preview.Install.MatchesTarget);

        var retried = await rig.StartAsync(Tip(vanilla));

        Assert.Equal(JobStatus.Succeeded, retried.Status);
        Assert.False(rig.InstallHas("mods/a.txt"));
        Assert.Equal("base content", rig.ReadInstall("data/base.pak"));
        var install = await rig.StatusAsync(RepoRole.Install);
        Assert.Null(install.InterruptedSwitch);
        Assert.Equal(vanilla.BranchName, install.Branch);
    }

    [Fact(Timeout = 150_000)]
    public async Task A_launch_cuts_in_front_of_a_running_optimization_which_ends_as_stopped_not_failed()
    {
        var (rig, _, modded) = await LaunchRig.WithTwoProfilesAsync();
        using var _ = rig;
        await rig.Workspace.WaitForIdleAsync(TimeSpan.FromSeconds(60));
        using var reached = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        rig.Workspace.Disk.FreePerPath = path =>
        {
            if (path.StartsWith(rig.Workspace.DefaultStorageRoot, StringComparison.OrdinalIgnoreCase) && !release.IsSet)
            {
                reached.Set();
                release.Wait(_held);
            }

            return null;
        };
        JobSnapshot optimization;
        JobSnapshot launch;
        try
        {
            var optimizing = new OptimizationService(rig.Workspace.Backend.Services).Start(rig.Game.Id, OptimizationMode.Routine, preemptible: true);
            Assert.True(reached.Wait(_held, Ct), "the optimization never reached the engine");

            var started = await rig.Launch.StartAsync(rig.Game.Id, Tip(modded));
            Assert.True(started.Ok, started.Error?.Detail);
            await Task.Delay(200, Ct);
            release.Set();
            optimization = await rig.WaitForJobAsync(optimizing);
            launch = await rig.WaitForJobAsync(started.Value.JobId);
        }
        finally
        {
            release.Set();
        }

        Assert.Equal(JobStatus.Cancelled, optimization.Status);
        Assert.Equal("error.compaction.preempted", optimization.Error?.MessageId);
        Assert.Equal(JobStatus.Succeeded, launch.Status);
    }
}
