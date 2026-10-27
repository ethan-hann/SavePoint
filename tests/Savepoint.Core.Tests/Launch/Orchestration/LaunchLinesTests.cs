using Savepoint.Core.Api;
using Savepoint.Core.Catalog;

namespace Savepoint.Core.Tests.Launch.Orchestration;

// What a launch writes into its details: each folder it switches with the files written and removed, the folders it leaves, and the start.
public class LaunchLinesTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static IReadOnlyList<JobLine.Step> Steps(JobSnapshot job) => [.. job.RecentLines.OfType<JobLine.Step>()];

    private static JobLine.Step Switch(RepoRole folder, string profile, string older, int written, int removed) =>
        Assert.IsType<JobLine.Step>(JobLine.ForStep(
            JobSteps.LaunchSwitchFolder,
            ("folder", folder),
            ("profile", profile),
            ("older", older),
            ("counted", "yes"),
            ("written", written),
            ("removed", removed)));

    [Fact(Timeout = 180_000)]
    public async Task A_switch_names_the_profile_each_folder_and_the_files_it_writes_and_removes_then_the_start()
    {
        var (rig, vanilla, _) = await LaunchRig.WithTwoProfilesAsync();
        using var _ = rig;

        // Going back to Vanilla rewrites one install file and removes the two mod files, and removes the second save.
        var job = await rig.StartAsync(new LaunchTarget.ProfileTip(vanilla.Id));

        Assert.Equal(
            [
                Switch(RepoRole.Install, "Vanilla", "no", written: 1, removed: 2),
                Switch(RepoRole.External, "Vanilla", "no", written: 0, removed: 1),
                JobLine.ForStep(JobSteps.LaunchStarting, ("game", "Play")),
            ],
            Steps(job));
    }

    [Fact(Timeout = 180_000)]
    public async Task A_launch_job_names_the_profile_it_starts()
    {
        var (rig, vanilla, _) = await LaunchRig.WithTwoProfilesAsync();
        using var _ = rig;

        var job = await rig.StartAsync(new LaunchTarget.ProfileTip(vanilla.Id));

        Assert.Equal(new JobProfile(vanilla.Id, "Vanilla"), job.Profile);
    }

    [Fact(Timeout = 180_000)]
    public async Task A_folder_that_already_holds_the_profile_says_so_and_is_not_switched()
    {
        var (rig, _, modded) = await LaunchRig.WithTwoProfilesAsync();
        using var _ = rig;

        var job = await rig.StartAsync(new LaunchTarget.ProfileTip(modded.Id));

        Assert.Equal(
            [
                JobLine.ForStep(JobSteps.LaunchFolderInPlace, ("folder", RepoRole.Install), ("profile", "Modded")),
                JobLine.ForStep(JobSteps.LaunchFolderInPlace, ("folder", RepoRole.External), ("profile", "Modded")),
                JobLine.ForStep(JobSteps.LaunchStarting, ("game", "Play")),
            ],
            Steps(job));
    }

    [Fact(Timeout = 180_000)]
    public async Task Starting_from_an_older_snapshot_says_the_folder_goes_to_that_snapshot()
    {
        var (rig, _, modded) = await LaunchRig.WithTwoProfilesAsync();
        using var _ = rig;
        await GitClock.NextSecondAsync(Ct);
        rig.WriteSave("slot3.sav", "third save");
        await rig.CaptureAsync(modded, "Third save");
        var older = (await rig.PreviewAsync(new LaunchTarget.ProfileTip(modded.Id))).AvailableSaves[1];

        var job = await rig.StartAsync(new LaunchTarget.Save(modded.Id, older.Id));

        Assert.Equal(
            [
                JobLine.ForStep(JobSteps.LaunchFolderInPlace, ("folder", RepoRole.Install), ("profile", "Modded")),
                Switch(RepoRole.External, "Modded", "yes", written: 0, removed: 1),
                JobLine.ForStep(JobSteps.LaunchStarting, ("game", "Play")),
            ],
            Steps(job));
    }

    // The first profile sees the point; the second was made from the same state, so the saves folder sits on the second one's branch.
    private static async Task<(LaunchRig Rig, HistoryPointId ModUpdate)> WithSavesLeftOnAnotherProfileAsync()
    {
        var rig = await LaunchRig.CreateAsync(withSaves: false, gameArguments: ["--exit-after", "800"]);
        try
        {
            var vanilla = await rig.ProfileAsync("Vanilla");
            rig.WriteInstall("mods/update.txt", "a mod update");
            await rig.CaptureAsync(vanilla, "Mod update", RepoRole.Install);
            await rig.AddSavesFolderAsync();
            await rig.CaptureAsync(vanilla, "First saves", RepoRole.External);
            await rig.ProfileAsync("Other");
            var modUpdate = (await rig.History.GetAsync(rig.Game.Id, null, rig.Token)).Value!.Points.Single(point => point.Name == "Mod update");
            return (rig, modUpdate.Id);
        }
        catch
        {
            rig.Dispose();
            throw;
        }
    }

    [Fact(Timeout = 180_000)]
    public async Task A_folder_the_snapshot_does_not_cover_is_named_as_not_included()
    {
        var (rig, modUpdate) = await WithSavesLeftOnAnotherProfileAsync();
        using var _ = rig;

        var job = await rig.StartAsync(new LaunchTarget.Point(modUpdate));

        Assert.Contains(JobLine.ForStep(JobSteps.LaunchFolderNotIncluded, ("folder", RepoRole.External)), Steps(job));
        Assert.DoesNotContain(Steps(job), step => step.MessageId == JobSteps.LaunchFolderInPlace && Equals(step.Args["folder"], "external"));
    }

    [Fact(Timeout = 180_000)]
    public async Task The_snapshot_taken_after_play_names_a_folder_it_left_out()
    {
        var (rig, modUpdate) = await WithSavesLeftOnAnotherProfileAsync();
        using var _ = rig;
        await rig.StartAsync(new LaunchTarget.Point(modUpdate));
        await rig.WaitForAsync<SessionState.Exited>();
        rig.WriteInstall("settings.ini", "played");

        Assert.True((await rig.Launch.CompleteSessionAsync(rig.Game.Id, new SessionCompletion.CaptureNew("Played"))).Ok);
        await rig.WaitForAsync<SessionState.Completed>();

        var capture = rig.Jobs.List().Last(job => job.Kind == JobKind.Capture);
        Assert.Contains(JobLine.ForStep(JobSteps.CaptureFolderNotIncluded, ("folder", RepoRole.External)), Steps(capture));
        Assert.Contains(Steps(capture), step => step.MessageId == JobSteps.CaptureFolderDone && Equals(step.Args["folder"], "install"));
    }
}
