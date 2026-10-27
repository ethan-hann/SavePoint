using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Launch;

namespace Savepoint.Core.Tests.Launch.Orchestration;

// A Steam game started from its program quits when the Steam client is closed, and starting it through Steam would let Steam install a
// pending update over the version being loaded. So the launch asks for the client before it reads or switches anything.
public class LaunchSteamTests
{
    private const int _appId = 2379780;

    private static LaunchTarget Tip(Profile profile) => new LaunchTarget.ProfileTip(profile.Id);

    private static async Task<(LaunchRig Rig, Profile Vanilla, Profile Modded)> SteamRigAsync(FakeSteamClient steam, SteamWaitOptions? wait = null, bool steamGame = true)
    {
        var (rig, vanilla, modded) = await LaunchRig.WithTwoProfilesAsync(steam: steam, steamWait: wait);
        if (steamGame)
        {
            await rig.EditGameAsync(game => game with { SteamAppId = _appId });
        }

        return (rig, vanilla, modded);
    }

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
    public async Task A_game_that_is_not_a_Steam_game_never_asks_about_Steam()
    {
        var steam = new FakeSteamClient();
        var (rig, vanilla, _) = await SteamRigAsync(steam, steamGame: false);
        using var _ = rig;

        var job = await rig.StartAsync(Tip(vanilla));
        await rig.WaitForGameAsync();

        Assert.Equal(JobStatus.Succeeded, job.Status);
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
        Assert.False(rig.InstallHas("mods/a.txt"));
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
