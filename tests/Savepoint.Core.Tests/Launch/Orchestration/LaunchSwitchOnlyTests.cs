using System.Diagnostics;
using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Git;
using Savepoint.Core.Tests.Git.Engine;
using Savepoint.Core.Tests.Services.Exe;

namespace Savepoint.Core.Tests.Launch.Orchestration;

// Switching without playing: the folders are checked and switched exactly as a launch does, and nothing starts or stays open afterwards.
public class LaunchSwitchOnlyTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static LaunchTarget Tip(Profile profile) => new LaunchTarget.ProfileTip(profile.Id);

    [Fact(Timeout = 180_000)]
    public async Task A_switch_loads_both_folders_and_ends_without_starting_the_game_or_opening_a_session()
    {
        var (rig, vanilla, _) = await LaunchRig.WithTwoProfilesAsync();
        using var _ = rig;
        var services = rig.Workspace.Backend.Services;

        var job = await rig.SwitchAsync(Tip(vanilla));

        Assert.Equal(JobStatus.Succeeded, job.Status);
        Assert.Equal(JobKind.Switch, job.Kind);
        Assert.Equal(rig.Game.Id, job.GameId);
        Assert.Equal(vanilla.Id, job.Profile?.Id);

        // Both folders hold Vanilla.
        Assert.False(rig.InstallHas("mods/a.txt"));
        Assert.Equal("base content", rig.ReadInstall("data/base.pak"));
        Assert.False(rig.SavesHave("slot2.sav"));

        // Nothing was started, nothing is open, and nothing was recorded as played.
        Assert.False(File.Exists(rig.ReadyFile));
        Assert.Empty(rig.GameProcessIds());
        Assert.Null(rig.Launch.GetSession(rig.Game.Id));
        Assert.Empty(rig.Launch.ListSessions());
        Assert.Empty(rig.Events);
        Assert.False(services.Sessions.IsGameRunning(rig.Game.Id));
        Assert.Empty(services.Sessions.RunningGameIds());
        Assert.Null((await rig.Profiles.ListAsync(rig.Game.Id, Ct)).Value!.Single(profile => profile.Id == vanilla.Id).LastLaunchedAt);

        // The job tells the switch the way a launch's does, and never says the game is starting.
        var steps = job.RecentLines.OfType<JobLine.Step>().Select(step => step.MessageId).ToList();
        Assert.Contains(JobSteps.LaunchSwitchFolder, steps);
        Assert.DoesNotContain(JobSteps.LaunchStarting, steps);

        // The profile reads as loaded now, from the folders alone.
        var rows = (await rig.Profiles.GetRowStatsAsync(rig.Game.Id, Ct)).Value!;
        Assert.True(rows.Single(row => row.ProfileId == vanilla.Id).IsActive);
        Assert.False(rows.Single(row => row.ProfileId == vanilla.Id).IsRunning);
    }

    [Fact(Timeout = 180_000)]
    public async Task A_snapshot_can_be_loaded_the_same_way()
    {
        var (rig, vanilla, _) = await LaunchRig.WithTwoProfilesAsync();
        using var _ = rig;
        var point = (await rig.History.GetAsync(rig.Game.Id, null, Ct)).Value!.Points.First(candidate => candidate.ProfileId == vanilla.Id);

        var job = await rig.SwitchAsync(new LaunchTarget.Point(point.Id));

        Assert.Equal(JobStatus.Succeeded, job.Status);
        Assert.False(rig.InstallHas("mods/a.txt"));
        Assert.Equal("base content", rig.ReadInstall("data/base.pak"));
        Assert.Null(rig.Launch.GetSession(rig.Game.Id));
        Assert.Empty(rig.GameProcessIds());
    }

    [Fact(Timeout = 180_000)]
    public async Task A_switch_over_unsaved_work_fails_as_a_launch_does_and_changes_nothing()
    {
        var (rig, vanilla, modded) = await LaunchRig.WithTwoProfilesAsync();
        using var _ = rig;
        var installBefore = rig.SnapshotOf(RepoRole.Install);
        rig.WriteSave("slot1.sav", "played on, never saved");

        var job = await rig.SwitchAsync(Tip(vanilla));

        Assert.Equal(JobStatus.Failed, job.Status);
        Assert.Equal("error.launch.dirtyWorkingTree", job.Error?.MessageId);
        Assert.Equal("external", job.Error!.Args["role"]);
        Assert.Equal(installBefore, rig.SnapshotOf(RepoRole.Install));
        Assert.Equal(modded.BranchName, (await rig.StatusAsync(RepoRole.Install)).Branch);
        Assert.False(rig.Workspace.Backend.Services.Sessions.IsGameRunning(rig.Game.Id));
    }

    [Theory(Timeout = 150_000)]
    [MemberData(nameof(HeldFileKinds))]
    public async Task A_file_another_program_holds_stops_the_switch_by_name_and_a_second_try_finishes_it(HeldFileKind kind)
    {
        var (rig, vanilla, _) = await LaunchRig.WithTwoProfilesAsync();
        using var _ = rig;
        await rig.WithEngineAsync(
            (_, repo) =>
            {
                HeldFiles.MakeRacy(repo);
                return Task.FromResult(true);
            },
            RepoRole.External);
        var held = Path.Combine(rig.SavesPath, "slot2.sav");

        JobSnapshot failed;
        using (HeldFiles.Hold(held, kind))
        {
            failed = await rig.SwitchAsync(Tip(vanilla));
        }

        Assert.Equal(JobStatus.Failed, failed.Status);
        Assert.Equal(HeldFiles.ErrorIdOf(kind), failed.Error?.MessageId);
        Assert.Equal(held, failed.Error!.Args["path"]);
        Assert.True(rig.InstallHas("mods/a.txt"), "nothing is switched while a file stands in the way");

        var retried = await rig.SwitchAsync(Tip(vanilla));

        Assert.Equal(JobStatus.Succeeded, retried.Status);
        Assert.False(rig.InstallHas("mods/a.txt"));
        Assert.False(rig.SavesHave("slot2.sav"));
    }

    public static TheoryData<HeldFileKind> HeldFileKinds => HeldFiles.Kinds;

    [Fact(Timeout = 240_000)]
    public async Task A_switch_is_refused_while_the_game_is_running_and_starts_no_job()
    {
        var (rig, vanilla, modded) = await LaunchRig.WithTwoProfilesAsync();
        using var _ = rig;

        // The game is running from a launch.
        var launch = await rig.StartAsync(Tip(modded));
        await rig.WaitForGameAsync();
        Assert.Equal(JobStatus.Succeeded, launch.Status);
        var jobsBefore = rig.Jobs.List().Count;

        var refused = await rig.Launch.SwitchAsync(rig.Game.Id, Tip(vanilla));

        Assert.Equal("error.launch.alreadyRunning", refused.Error?.MessageId);
        Assert.Equal(jobsBefore, rig.Jobs.List().Count);
        Assert.True(rig.InstallHas("mods/a.txt"));
    }

    [Fact(Timeout = 180_000)]
    public async Task A_game_the_player_started_by_hand_refuses_the_switch_before_any_job_starts()
    {
        var (rig, vanilla, _) = await LaunchRig.WithTwoProfilesAsync(followProcessName: true);
        using var _ = rig;
        var ready = Path.Combine(rig.Workspace.Root, "by-hand-ready.txt");
        var exe = Path.Combine(rig.InstallPath, rig.ExeName + (OperatingSystem.IsWindows() ? ".exe" : string.Empty));
        using var byHand = Process.Start(new ProcessStartInfo(exe, ["--ready-file", ready]) { UseShellExecute = false, WorkingDirectory = rig.InstallPath });
        await FakeGameProcess.WaitForReadyFileAsync(ready);

        var refused = await rig.Launch.SwitchAsync(rig.Game.Id, Tip(vanilla));

        Assert.Equal("error.launch.alreadyRunning", refused.Error?.MessageId);
        Assert.DoesNotContain(rig.Jobs.List(), job => job.Kind == JobKind.Switch);
        Assert.True(rig.InstallHas("mods/a.txt"));
        Assert.False(rig.Workspace.Backend.Services.Sessions.IsGameRunning(rig.Game.Id), "a refused switch must not leave the game claimed");
    }

    [Fact(Timeout = 240_000)]
    public async Task A_switch_waiting_for_the_game_keeps_it_taken_and_cancelling_it_changes_nothing_and_frees_the_game()
    {
        var (rig, vanilla, modded) = await LaunchRig.WithTwoProfilesAsync();
        using var _ = rig;
        var services = rig.Workspace.Backend.Services;
        using var held = new HeldCapture(rig);
        rig.WriteSave("slot3.sav", "three");
        var capture = await rig.Profiles.CaptureAsync(rig.Game.Id, modded.Id, "Third");
        Assert.True(capture.Ok, capture.Error?.Detail);
        Assert.True(held.Reached(rig.Token), "the capture never reached the engine");
        var installBefore = rig.SnapshotOf(RepoRole.Install);

        var started = await rig.Launch.SwitchAsync(rig.Game.Id, Tip(vanilla));
        Assert.True(started.Ok, started.Error?.Detail);

        // Waiting for the lock, the switch already holds the game: nothing else may begin for it.
        Assert.True(services.Sessions.IsGameRunning(rig.Game.Id));
        // The game is not running, so the refusals say a switch is under way and not that the game runs.
        Assert.Equal("error.gameService.switching", (await rig.Launch.StartAsync(rig.Game.Id, Tip(vanilla))).Error?.MessageId);
        Assert.Equal("error.gameService.switching", (await rig.Launch.SwitchAsync(rig.Game.Id, Tip(vanilla))).Error?.MessageId);
        var refused = await rig.Launch.DiscardChangesAsync(rig.Game.Id, RepoRole.External);
        Assert.Equal("error.gameService.switching", refused.Error?.MessageId);
        Assert.Equal("Play", refused.Error!.Args["gameName"]);

        Assert.True((await rig.Jobs.CancelAsync(started.Value.JobId)).Ok);
        held.Release();
        await rig.WaitForJobAsync(capture.Value.JobId);
        var job = await rig.WaitForJobAsync(started.Value.JobId);

        Assert.Equal(JobStatus.Cancelled, job.Status);
        Assert.Equal("error.launch.canceled", job.Error?.MessageId);
        Assert.Equal(installBefore, rig.SnapshotOf(RepoRole.Install));
        Assert.False(services.Sessions.IsGameRunning(rig.Game.Id));
        Assert.True((await rig.Launch.SwitchAsync(rig.Game.Id, Tip(vanilla))).Ok);
    }

    [Fact(Timeout = 240_000)]
    public async Task A_switch_stopped_at_the_engine_leaves_the_folder_unfinished_frees_the_game_and_the_next_switch_to_the_same_target_finishes_it()
    {
        var (rig, vanilla, modded) = await LaunchRig.WithTwoProfilesAsync();
        using var _ = rig;
        var services = rig.Workspace.Backend.Services;
        using (var reached = new ManualResetEventSlim())
        using (var release = new ManualResetEventSlim())
        {
            // The first question is the check made before any folder is switched; the switch itself asks second, and that is where it is held.
            var installQuestions = 0;
            rig.Workspace.Disk.FreePerPath = path =>
            {
                if (path.StartsWith(rig.InstallPath, StringComparison.OrdinalIgnoreCase) && Interlocked.Increment(ref installQuestions) == 2 && !release.IsSet)
                {
                    reached.Set();
                    release.Wait(TimeSpan.FromSeconds(20));
                }

                return null;
            };
            try
            {
                var started = await rig.Launch.SwitchAsync(rig.Game.Id, Tip(vanilla));
                Assert.True(started.Ok, started.Error?.Detail);
                Assert.True(reached.Wait(TimeSpan.FromSeconds(20), Ct), "the switch never reached the engine");
                await rig.Jobs.CancelAsync(started.Value.JobId);
                release.Set();
                var stopped = await rig.WaitForJobAsync(started.Value.JobId);
                Assert.Equal(JobStatus.Cancelled, stopped.Status);
                Assert.Equal("error.launch.canceled", stopped.Error?.MessageId);
            }
            finally
            {
                release.Set();
                rig.Workspace.Disk.FreePerPath = null;
            }
        }

        // Stopped part way: the folder says so, the game is free, and no session or game was ever opened.
        Assert.NotNull((await rig.StatusAsync(RepoRole.Install)).InterruptedSwitch);
        Assert.False(services.Sessions.IsGameRunning(rig.Game.Id));
        Assert.Null(rig.Launch.GetSession(rig.Game.Id));

        // A play to another target is refused until the switch is finished or discarded.
        var refused = await rig.StartAsync(Tip(modded));
        Assert.Equal(JobStatus.Failed, refused.Status);
        Assert.Equal("error.launch.switchInterrupted", refused.Error?.MessageId);
        Assert.Equal("install", refused.Error!.Args["role"]);
        Assert.Empty(rig.GameProcessIds());

        // The same target finishes it, as a switch, with the folders on Vanilla and no game started.
        var finished = await rig.SwitchAsync(Tip(vanilla));
        Assert.Equal(JobStatus.Succeeded, finished.Status);
        var install = await rig.StatusAsync(RepoRole.Install);
        Assert.Null(install.InterruptedSwitch);
        Assert.Equal(vanilla.BranchName, install.Branch);
        Assert.False(rig.InstallHas("mods/a.txt"));
        Assert.Empty(rig.GameProcessIds());
        Assert.False(services.Sessions.IsGameRunning(rig.Game.Id));
    }

    // Holds the next capture at its first free-space question, with the game's lock, so a switch started meanwhile has to queue.
    private sealed class HeldCapture : IDisposable
    {
        private static readonly TimeSpan _held = TimeSpan.FromSeconds(20);
        private readonly ManualResetEventSlim _reached = new();
        private readonly ManualResetEventSlim _release = new();

        public HeldCapture(LaunchRig rig)
        {
            rig.Workspace.Disk.FreePerPath = path =>
            {
                if (path.StartsWith(rig.Workspace.DefaultStorageRoot, StringComparison.OrdinalIgnoreCase)
                    && new StackTrace().ToString().Contains("CaptureCoreAsync", StringComparison.Ordinal))
                {
                    _reached.Set();
                    _release.Wait(_held);
                }

                return null;
            };
        }

        public bool Reached(CancellationToken token) => _reached.Wait(_held, token);

        public void Release() => _release.Set();

        public void Dispose()
        {
            _release.Set();
            _reached.Dispose();
            _release.Dispose();
        }
    }
}
