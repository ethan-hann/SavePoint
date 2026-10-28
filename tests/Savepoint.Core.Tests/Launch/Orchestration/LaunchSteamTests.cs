using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Launch;
using Savepoint.Core.Launch.Orchestration;
using Savepoint.Core.Platform;

namespace Savepoint.Core.Tests.Launch.Orchestration;

// A Steam game started from its program quits when the Steam client is closed, and starting it through Steam would let Steam install a
// pending update over the version being loaded. So the launch asks for the client before it reads or switches anything.
public class LaunchSteamTests
{
    private const int _appId = 2379780;

    // StateFlags as Steam writes them: 4 is fully installed, 2 adds that an update is waiting, 1024 adds that an update has started.
    private const long _installed = 4;
    private const long _updating = 4 | 1024;
    private const long _updateWaiting = 2 | 4;

    private static LaunchTarget Tip(Profile profile) => new LaunchTarget.ProfileTip(profile.Id);

    // A Steam game is installed in a Steam library. The other game is a GOG or retail copy: it carries the Steam app id of the template it
    // matched, but sits outside any library.
    private static async Task<(LaunchRig Rig, Profile Vanilla, Profile Modded)> SteamRigAsync(FakeSteamClient steam, SteamWaitOptions? wait = null, bool steamGame = true)
    {
        var (rig, vanilla, modded) = await LaunchRig.WithTwoProfilesAsync(steam: steam, steamWait: wait, steamLibrary: steamGame);
        if (!steamGame)
        {
            await rig.EditGameAsync(game => game with { SteamAppId = _appId });
        }

        return (rig, vanilla, modded);
    }

    // Steam's manifest for a rig in a library: fully installed unless a test says Steam is updating.
    private static void Manifest(LaunchRig rig, long flags = _installed, string buildId = "100", string lastUpdated = "1") =>
        rig.Workspace.WriteSteamManifest(_appId, buildId, flags, lastUpdated);

    private static bool Switched(LaunchRig rig) =>
        rig.Jobs.List().Where(job => job.GameId == rig.Game.Id)
            .Any(job => job.RecentLines.OfType<JobLine.Step>().Any(step => step.MessageId == JobSteps.LaunchSwitchFolder));

    [Fact(Timeout = 240_000)]
    public async Task A_Steam_game_with_Steam_closed_is_refused_at_once_before_any_folder_is_read()
    {
        var steam = new FakeSteamClient();
        var (rig, vanilla, _) = await SteamRigAsync(steam);
        using var _ = rig;
        var installBefore = rig.SnapshotOf(RepoRole.Install);
        var jobsBefore = rig.Jobs.List().Count;

        // Unsaved work would stop a launch that got as far as reading the folders; the Steam question comes first.
        rig.WriteSave("slot1.sav", "played on, never saved");
        var refused = await rig.Launch.StartAsync(rig.Game.Id, Tip(vanilla));

        Assert.False(refused.Ok);
        Assert.Equal("error.launch.steamNotRunning", refused.Error?.MessageId);
        Assert.Equal(jobsBefore, rig.Jobs.List().Count);
        Assert.Null(rig.Launch.GetSession(rig.Game.Id));
        Assert.Equal(installBefore, rig.SnapshotOf(RepoRole.Install));
        Assert.Empty(rig.GameProcessIds());
        Assert.Equal(0, steam.Starts);
    }

    [Fact(Timeout = 240_000)]
    public async Task A_Steam_game_with_Steam_running_launches_and_switches_as_before()
    {
        var steam = new FakeSteamClient(ready: true);
        var (rig, vanilla, _) = await SteamRigAsync(steam);
        using var _ = rig;

        var job = await rig.StartAsync(Tip(vanilla));
        await rig.WaitForGameAsync();

        Assert.Equal(JobStatus.Succeeded, job.Status);
        Assert.False(rig.InstallHas("mods/a.txt"));
        Assert.Equal(0, steam.Starts);
    }

    [Fact(Timeout = 240_000)]
    public async Task A_GOG_copy_with_a_Steam_app_id_outside_a_Steam_library_never_asks_about_Steam()
    {
        var steam = new FakeSteamClient();
        var (rig, vanilla, _) = await SteamRigAsync(steam, steamGame: false);
        using var _ = rig;

        Assert.Null((await rig.PreviewAsync(Tip(vanilla))).SteamNeeded);
        var job = await rig.StartAsync(Tip(vanilla));
        await rig.WaitForGameAsync();

        Assert.Equal(JobStatus.Succeeded, job.Status);
        Assert.Equal(0, steam.Starts);
    }

    [Fact(Timeout = 240_000)]
    public async Task Play_without_Steam_starts_the_program_with_Steam_closed_and_never_starts_Steam()
    {
        var steam = new FakeSteamClient();
        var (rig, vanilla, _) = await SteamRigAsync(steam);
        using var _ = rig;

        var started = await rig.Launch.StartAsync(rig.Game.Id, Tip(vanilla), withoutSteam: true);
        Assert.True(started.Ok, started.Error?.Detail);
        var job = await rig.WaitForJobAsync(started.Value!.JobId);
        await rig.WaitForGameAsync();

        Assert.Equal(JobStatus.Succeeded, job.Status);
        Assert.NotEmpty(rig.GameProcessIds());
        Assert.Equal(0, steam.Starts);
        Assert.DoesNotContain(JobSteps.LaunchWaitingForSteam, job.RecentLines.OfType<JobLine.Step>().Select(step => step.MessageId));
    }

    [Fact(Timeout = 240_000)]
    public async Task A_Steam_update_that_starts_as_the_client_comes_up_stops_the_launch_before_the_program_starts()
    {
        LaunchRig? rig = null;
        var steam = new FakeSteamClient { ReadyAfterLooks = 1, OnStart = () => Manifest(rig!, _updating) };
        var (made, vanilla, _) = await SteamRigAsync(steam);
        rig = made;
        using var _ = rig;
        Manifest(rig);

        var started = await rig.Launch.StartAsync(rig.Game.Id, Tip(vanilla), startSteam: true);
        var job = await rig.WaitForJobAsync(started.Value!.JobId);

        Assert.Equal(JobStatus.Failed, job.Status);
        Assert.Equal("error.launch.steamUpdatingBeforeSwitch", job.Error?.MessageId);
        Assert.Empty(rig.GameProcessIds());
        Assert.False(Switched(rig));
        var session = await rig.WaitForAsync<SessionState.Failed>();
        Assert.Equal("error.launch.steamUpdatingBeforeSwitch", Assert.IsType<SessionState.Failed>(session.State).Error.MessageId);
    }

    [Fact(Timeout = 240_000)]
    public async Task A_new_Steam_build_written_while_the_client_comes_up_stops_the_launch_before_the_program_starts()
    {
        LaunchRig? rig = null;
        var steam = new FakeSteamClient { ReadyAfterLooks = 1, OnStart = () => Manifest(rig!, _installed, buildId: "101", lastUpdated: "2") };
        var (made, vanilla, _) = await SteamRigAsync(steam);
        rig = made;
        using var _ = rig;
        Manifest(rig);

        var started = await rig.Launch.StartAsync(rig.Game.Id, Tip(vanilla), startSteam: true);
        var job = await rig.WaitForJobAsync(started.Value!.JobId);

        Assert.Equal(JobStatus.Failed, job.Status);
        Assert.Equal("error.launch.steamUpdatingBeforeSwitch", job.Error?.MessageId);
        Assert.Empty(rig.GameProcessIds());
        Assert.False(Switched(rig));
    }

    [Fact(Timeout = 240_000)]
    public async Task A_Steam_update_already_running_when_Steam_is_up_stops_the_launch_before_any_folder_is_switched()
    {
        var steam = new FakeSteamClient(ready: true);
        var (rig, vanilla, _) = await SteamRigAsync(steam);
        using var _ = rig;
        Manifest(rig, _updating);
        var installBefore = rig.SnapshotOf(RepoRole.Install);
        var savesBefore = rig.SnapshotOf(RepoRole.External);

        var job = await StartAndWaitAsync(rig, Tip(vanilla));

        Assert.Equal(JobStatus.Failed, job.Status);
        Assert.Equal("error.launch.steamUpdatingBeforeSwitch", job.Error?.MessageId);
        Assert.Empty(rig.GameProcessIds());
        Assert.False(Switched(rig));
        Assert.Equal(installBefore, rig.SnapshotOf(RepoRole.Install));
        Assert.Equal(savesBefore, rig.SnapshotOf(RepoRole.External));
    }

    [Fact(Timeout = 240_000)]
    public async Task A_stale_update_flag_left_by_a_crashed_update_does_not_block_a_launch_that_starts_Steam()
    {
        // With Steam closed the flag may be left over from an update that never finished; starting the client clears it.
        LaunchRig? rig = null;
        var steam = new FakeSteamClient { ReadyAfterLooks = 1, OnStart = () => Manifest(rig!, _installed) };
        var (made, vanilla, _) = await SteamRigAsync(steam);
        rig = made;
        using var _ = rig;
        Manifest(rig, _updating);

        var started = await rig.Launch.StartAsync(rig.Game.Id, Tip(vanilla), startSteam: true);
        var job = await rig.WaitForJobAsync(started.Value!.JobId);
        await rig.WaitForGameAsync();

        Assert.Equal(JobStatus.Succeeded, job.Status);
        Assert.NotEmpty(rig.GameProcessIds());
    }

    [Fact(Timeout = 240_000)]
    public async Task A_Steam_update_that_starts_while_the_folders_are_switched_stops_the_program_and_says_the_files_may_have_changed()
    {
        // The second look at the process list is the launch's own check inside the job, after Steam was found ready and before the switch.
        LaunchRig? rig = null;
        var inspector = new TriggeredInspector(onSecondLook: () => rig!.Workspace.WriteSteamManifest(_appId, "100", _updating, "1"));
        var starter = new GameStarter(GameLaunchers.ForCurrentOs(), inspector, PlatformFactory.CurrentOs, new GameSessionOptions { PollInterval = TimeSpan.FromMilliseconds(100), Grace = TimeSpan.FromSeconds(3) }, TimeProvider.System);
        var steam = new FakeSteamClient(ready: true);
        var (made, vanilla, _) = await LaunchRig.WithTwoProfilesAsync(
            configure: options => options with { Launch = LaunchOrchestrator.Factory(new LaunchOrchestratorOptions { Starter = starter, Steam = steam }) },
            followProcessName: true,
            steam: steam,
            steamLibrary: true);
        rig = made;
        using var _ = rig;
        Manifest(rig);

        var job = await StartAndWaitAsync(rig, Tip(vanilla));

        Assert.Equal(JobStatus.Failed, job.Status);
        Assert.Equal("error.launch.steamUpdatingGame", job.Error?.MessageId);
        Assert.True(Switched(rig), "the update began after the first folder was read, so the switch had already happened");
        Assert.Empty(rig.GameProcessIds());
    }

    // A process list that is empty and runs a test's action at its second look.
    private sealed class TriggeredInspector(Action onSecondLook) : IProcessInspector
    {
        private int _looks;

        public IReadOnlyList<RunningProcess> List()
        {
            if (Interlocked.Increment(ref _looks) == 2)
            {
                onSecondLook();
            }

            return [];
        }
    }

    [Fact(Timeout = 240_000)]
    public async Task A_Steam_game_whose_manifest_stays_as_it_was_starts_even_with_an_update_waiting()
    {
        LaunchRig? rig = null;
        var steam = new FakeSteamClient { ReadyAfterLooks = 1, OnStart = () => Manifest(rig!, _updateWaiting) };
        var (made, vanilla, _) = await SteamRigAsync(steam);
        rig = made;
        using var _ = rig;
        Manifest(rig, _updateWaiting);

        var started = await rig.Launch.StartAsync(rig.Game.Id, Tip(vanilla), startSteam: true);
        var job = await rig.WaitForJobAsync(started.Value!.JobId);
        await rig.WaitForGameAsync();

        Assert.Equal(JobStatus.Succeeded, job.Status);
        Assert.NotEmpty(rig.GameProcessIds());
    }

    [Fact(Timeout = 240_000)]
    public async Task Play_without_Steam_skips_the_update_check_because_Steam_is_closed()
    {
        var steam = new FakeSteamClient();
        var (rig, vanilla, _) = await SteamRigAsync(steam);
        using var _ = rig;
        Manifest(rig, _updating);

        var started = await rig.Launch.StartAsync(rig.Game.Id, Tip(vanilla), withoutSteam: true);
        var job = await rig.WaitForJobAsync(started.Value!.JobId);

        Assert.Equal(JobStatus.Succeeded, job.Status);
    }

    private static async Task<JobSnapshot> StartAndWaitAsync(LaunchRig rig, LaunchTarget target)
    {
        var started = await rig.Launch.StartAsync(rig.Game.Id, target);
        Assert.True(started.Ok, started.Error?.Detail);
        return await rig.WaitForJobAsync(started.Value.JobId);
    }

    [Fact(Timeout = 240_000)]
    public async Task Start_Steam_starts_the_client_once_waits_for_it_then_switches_and_starts_the_game()
    {
        var steam = new FakeSteamClient { ReadyAfterLooks = 3 };
        var (rig, vanilla, _) = await SteamRigAsync(steam);
        using var _ = rig;

        var started = await rig.Launch.StartAsync(rig.Game.Id, Tip(vanilla), startSteam: true);
        Assert.True(started.Ok, started.Error?.Detail);
        var job = await rig.WaitForJobAsync(started.Value!.JobId);
        await rig.WaitForGameAsync();

        Assert.Equal(JobStatus.Succeeded, job.Status);
        Assert.Equal(1, steam.Starts);
        var steps = job.RecentLines.OfType<JobLine.Step>().Select(step => step.MessageId).ToList();
        Assert.Contains(JobSteps.LaunchWaitingForSteam, steps);
        Assert.True(steps.IndexOf(JobSteps.LaunchWaitingForSteam) < steps.IndexOf(JobSteps.LaunchSwitchFolder), "the wait comes before the first folder is switched");

        // The wait ends the moment Steam is signed in, before the folders are read, so the screen can stop saying it is waiting.
        Assert.Contains(JobSteps.LaunchSteamReady, steps);
        Assert.True(steps.IndexOf(JobSteps.LaunchWaitingForSteam) < steps.IndexOf(JobSteps.LaunchSteamReady) && steps.IndexOf(JobSteps.LaunchSteamReady) < steps.IndexOf(JobSteps.LaunchSwitchFolder), "Steam is ready between the wait and the first switch");
        Assert.False(rig.InstallHas("mods/a.txt"));
    }

    [Fact(Timeout = 240_000)]
    public async Task A_game_that_quits_without_its_store_plays_once_the_launch_has_started_the_store_first()
    {
        // The stand-in game quits at once unless the variable is set, as a Steamworks game quits when its Steam library cannot start;
        // starting the client sets the variable, so the game only runs if it was started after Steam was up.
        var variable = "SAVEPOINT_FG_STEAM_" + Guid.NewGuid().ToString("N");
        var steam = new FakeSteamClient { ReadyAfterLooks = 2, OnStart = () => Environment.SetEnvironmentVariable(variable, "1") };
        var (rig, vanilla, _) = await LaunchRig.WithTwoProfilesAsync(gameArguments: ["--exit-unless-env", variable], steam: steam, steamLibrary: true);
        try
        {
            using var _ = rig;

            var started = await rig.Launch.StartAsync(rig.Game.Id, Tip(vanilla), startSteam: true);
            var job = await rig.WaitForJobAsync(started.Value!.JobId);
            await rig.WaitForGameAsync();

            Assert.Equal(JobStatus.Succeeded, job.Status);
            Assert.NotEmpty(rig.GameProcessIds());
            Assert.IsType<SessionState.Running>(rig.Launch.GetSession(rig.Game.Id)?.State);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variable, null);
        }
    }

    [Fact(Timeout = 240_000)]
    public async Task A_Steam_game_started_after_Start_Steam_and_quit_a_few_seconds_in_offers_the_after_play_choices_without_Im_done()
    {
        // The real grace is two minutes; a game that waited it out would still be Running when the wait for the end gives up.
        var session = new GameSessionOptions { PollInterval = TimeSpan.FromMilliseconds(100), Grace = TimeSpan.FromMinutes(5) };
        var steam = new FakeSteamClient { ReadyAfterLooks = 2 };
        var (rig, vanilla, _) = await LaunchRig.WithTwoProfilesAsync(
            configure: options => options with
            {
                Launch = LaunchOrchestrator.Factory(new LaunchOrchestratorOptions { Session = session, Steam = steam, SteamWait = new SteamWaitOptions { Timeout = TimeSpan.FromSeconds(5), PollInterval = TimeSpan.FromMilliseconds(20) } }),
            },
            gameArguments: ["--exit-after", "1500"],
            followProcessName: true,
            steam: steam,
            steamLibrary: true);
        using var _ = rig;

        var started = await rig.Launch.StartAsync(rig.Game.Id, Tip(vanilla), startSteam: true);
        Assert.True(started.Ok, started.Error?.Detail);
        await rig.WaitForJobAsync(started.Value!.JobId);

        var exited = Assert.IsType<SessionState.Exited>((await rig.WaitForAsync<SessionState.Exited>()).State);

        Assert.Equal(1, steam.Starts);
        Assert.Equal(SessionEndKind.ChildExited, exited.EndKind);
        Assert.Empty(rig.GameProcessIds());
    }

    [Fact(Timeout = 240_000)]
    public async Task Start_Steam_with_the_client_already_up_starts_nothing()
    {
        var steam = new FakeSteamClient(ready: true);
        var (rig, vanilla, _) = await SteamRigAsync(steam);
        using var _ = rig;

        var started = await rig.Launch.StartAsync(rig.Game.Id, Tip(vanilla), startSteam: true);
        var job = await rig.WaitForJobAsync(started.Value!.JobId);

        Assert.Equal(JobStatus.Succeeded, job.Status);
        Assert.Equal(0, steam.Starts);
        Assert.DoesNotContain(JobSteps.LaunchWaitingForSteam, job.RecentLines.OfType<JobLine.Step>().Select(step => step.MessageId));
    }

    [Fact(Timeout = 240_000)]
    public async Task A_client_that_never_signs_in_fails_the_launch_and_changes_nothing()
    {
        var steam = new FakeSteamClient();
        var (rig, vanilla, _) = await SteamRigAsync(steam, new SteamWaitOptions { Timeout = TimeSpan.FromMilliseconds(300), PollInterval = TimeSpan.FromMilliseconds(20) });
        using var _ = rig;
        var installBefore = rig.SnapshotOf(RepoRole.Install);
        var savesBefore = rig.SnapshotOf(RepoRole.External);

        var started = await rig.Launch.StartAsync(rig.Game.Id, Tip(vanilla), startSteam: true);
        var job = await rig.WaitForJobAsync(started.Value!.JobId);

        Assert.Equal(JobStatus.Failed, job.Status);
        Assert.Equal("error.launch.steamDidNotStart", job.Error?.MessageId);
        Assert.Equal(1, steam.Starts);
        Assert.False(Switched(rig));
        Assert.Equal(installBefore, rig.SnapshotOf(RepoRole.Install));
        Assert.Equal(savesBefore, rig.SnapshotOf(RepoRole.External));
        Assert.Empty(rig.GameProcessIds());
        var session = await rig.WaitForAsync<SessionState.Failed>();
        Assert.Equal("error.launch.steamDidNotStart", Assert.IsType<SessionState.Failed>(session.State).Error.MessageId);
    }

    [Fact(Timeout = 240_000)]
    public async Task A_client_that_cannot_be_started_fails_the_launch_without_waiting()
    {
        var steam = new FakeSteamClient { CanStart = false };
        var (rig, vanilla, _) = await SteamRigAsync(steam, new SteamWaitOptions { Timeout = TimeSpan.FromMinutes(5), PollInterval = TimeSpan.FromMilliseconds(20) });
        using var _ = rig;

        var started = await rig.Launch.StartAsync(rig.Game.Id, Tip(vanilla), startSteam: true);
        var job = await rig.WaitForJobAsync(started.Value!.JobId);

        Assert.Equal(JobStatus.Failed, job.Status);
        Assert.Equal("error.launch.steamDidNotStart", job.Error?.MessageId);
        Assert.False(Switched(rig));
    }

    [Fact(Timeout = 240_000)]
    public async Task Cancelling_the_wait_for_Steam_changes_nothing_and_starts_no_game()
    {
        var steam = new FakeSteamClient();
        var (rig, vanilla, _) = await SteamRigAsync(steam, new SteamWaitOptions { Timeout = TimeSpan.FromMinutes(5), PollInterval = TimeSpan.FromMilliseconds(20) });
        using var _ = rig;
        var installBefore = rig.SnapshotOf(RepoRole.Install);

        var started = await rig.Launch.StartAsync(rig.Game.Id, Tip(vanilla), startSteam: true);
        await WaitForWaitStepAsync(rig, started.Value!.JobId);
        await rig.Jobs.CancelAsync(started.Value!.JobId);
        var job = await rig.WaitForJobAsync(started.Value!.JobId);

        Assert.Equal(JobStatus.Cancelled, job.Status);
        Assert.False(Switched(rig));
        Assert.Equal(installBefore, rig.SnapshotOf(RepoRole.Install));
        Assert.Empty(rig.GameProcessIds());
        Assert.Equal(1, steam.Starts);
    }

    [Fact(Timeout = 240_000)]
    public async Task A_switch_without_play_never_asks_about_Steam()
    {
        var steam = new FakeSteamClient();
        var (rig, vanilla, _) = await SteamRigAsync(steam);
        using var _ = rig;

        var job = await rig.SwitchAsync(Tip(vanilla));

        Assert.Equal(JobStatus.Succeeded, job.Status);
        Assert.False(rig.InstallHas("mods/a.txt"));
        Assert.Equal(0, steam.Starts);
    }

    [Fact(Timeout = 240_000)]
    public async Task The_preview_says_Steam_is_needed_only_for_a_Steam_game_started_from_its_program_while_Steam_is_closed()
    {
        var steam = new FakeSteamClient();
        var (rig, vanilla, _) = await SteamRigAsync(steam);
        using var _ = rig;

        var closed = await rig.PreviewAsync(Tip(vanilla));
        Assert.Equal("error.launch.steamNotRunning", closed.SteamNeeded?.MessageId);

        // A link start is Steam's own to make, so the game is left alone however Steam is.
        await rig.EditGameAsync(game => game with { Launch = new LaunchConfig(LaunchMethod.Uri, Uri: "steam://rungameid/2379780") });
        Assert.Null((await rig.PreviewAsync(Tip(vanilla))).SteamNeeded);
    }

    [Fact(Timeout = 240_000)]
    public async Task The_preview_has_no_Steam_question_once_Steam_is_up_or_for_a_game_that_is_not_a_Steam_game()
    {
        var (open, vanillaOpen, _) = await SteamRigAsync(new FakeSteamClient(ready: true));
        using var _1 = open;
        var (plain, vanillaPlain, _) = await SteamRigAsync(new FakeSteamClient(), steamGame: false);
        using var _2 = plain;

        Assert.Null((await open.PreviewAsync(Tip(vanillaOpen))).SteamNeeded);
        Assert.Null((await plain.PreviewAsync(Tip(vanillaPlain))).SteamNeeded);
    }

    private static async Task WaitForWaitStepAsync(LaunchRig rig, Guid jobId)
    {
        var until = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (!(rig.Jobs.Get(jobId)?.RecentLines.OfType<JobLine.Step>().Any(step => step.MessageId == JobSteps.LaunchWaitingForSteam) ?? false))
        {
            Assert.True(DateTime.UtcNow < until, "the job never reached the wait for Steam");
            await Task.Delay(20, TestContext.Current.CancellationToken);
        }
    }
}
