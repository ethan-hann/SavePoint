using Savepoint.Core.Api;
using Savepoint.Core.Catalog;

namespace Savepoint.Core.Tests.Launch.Orchestration;

// A launch switches the mods folder with the install and the saves: install first, then mods, then saves.
public class ModsLaunchTests
{
    private static LaunchTarget Tip(Profile profile) => new LaunchTarget.ProfileTip(profile.Id);

    private static IReadOnlyList<JobLine.Step> Steps(JobSnapshot job) => [.. job.RecentLines.OfType<JobLine.Step>()];

    private static JobLine.Step Switch(RepoRole folder, string profile, int written, int removed) =>
        Assert.IsType<JobLine.Step>(JobLine.ForStep(
            JobSteps.LaunchSwitchFolder,
            ("folder", folder),
            ("profile", profile),
            ("older", "no"),
            ("counted", "yes"),
            ("written", written),
            ("removed", removed)));

    [Fact(Timeout = 240_000)]
    public async Task A_switch_writes_the_install_then_the_mods_then_the_saves_and_puts_the_mods_back()
    {
        var (rig, vanilla, _) = await ThreeFolderRig.WithTwoProfilesAndModsAsync();
        using var _ = rig;

        var job = await rig.StartAsync(Tip(vanilla));

        Assert.Equal(
            [
                Switch(RepoRole.Install, "Vanilla", written: 1, removed: 2),
                Switch(RepoRole.Mods, "Vanilla", written: 1, removed: 1),
                Switch(RepoRole.External, "Vanilla", written: 0, removed: 1),
            ],
            Steps(job).Where(step => step.MessageId == JobSteps.LaunchSwitchFolder));
        Assert.Equal("mod a", rig.ReadMods("modA/info.xml"));
        Assert.False(rig.ModsHave("modB/info.xml"));
        var mods = await rig.StatusAsync(RepoRole.Mods);
        Assert.Equal(vanilla.BranchName, mods.Branch);
        Assert.True(mods.IsClean);
    }

    [Fact(Timeout = 240_000)]
    public async Task A_folder_that_already_holds_the_profile_says_so_for_the_mods_folder_too()
    {
        var (rig, _, modded) = await ThreeFolderRig.WithTwoProfilesAndModsAsync();
        using var _ = rig;

        var job = await rig.StartAsync(Tip(modded));

        Assert.Contains(JobLine.ForStep(JobSteps.LaunchFolderInPlace, ("folder", RepoRole.Mods), ("profile", "Modded")), Steps(job));
    }

    [Fact(Timeout = 240_000)]
    public async Task Going_back_to_an_older_point_puts_the_mods_folder_back_to_that_point_as_well()
    {
        var (rig, _, modded) = await ThreeFolderRig.WithTwoProfilesAndModsAsync();
        using var _ = rig;
        var first = (await rig.History.GetAsync(rig.Game.Id, modded.Id, rig.Token)).Value!.Points.Single(point => point.Name == "Modded");
        rig.WriteMods("modC/info.xml", "mod c");
        await rig.CaptureAsync(modded, "Added mod c", RepoRole.Mods);

        var job = await rig.StartAsync(new LaunchTarget.Point(first.Id));

        Assert.Equal(JobStatus.Succeeded, job.Status);
        Assert.False(rig.ModsHave("modC/info.xml"));
        Assert.True(rig.ModsHave("modB/info.xml"));
    }

    [Fact(Timeout = 240_000)]
    public async Task A_point_only_the_mods_folder_recorded_is_found_and_the_other_folders_go_to_the_profiles_commit_at_that_time()
    {
        var (rig, vanilla, modded) = await ThreeFolderRig.WithTwoProfilesAndModsAsync();
        using var _ = rig;
        rig.WriteMods("modC/info.xml", "mod c");
        await rig.CaptureAsync(modded, "Added mod c", RepoRole.Mods);
        var modsOnly = (await rig.History.GetAsync(rig.Game.Id, modded.Id, rig.Token)).Value!.Points.Single(point => point.Name == "Added mod c");
        await rig.SwitchAsync(Tip(vanilla));

        var job = await rig.SwitchAsync(new LaunchTarget.Point(modsOnly.Id));

        Assert.Equal(JobStatus.Succeeded, job.Status);
        Assert.True(rig.ModsHave("modC/info.xml"));
        Assert.True(rig.InstallHas("mods/a.txt"));
        Assert.Equal(modded.BranchName, (await rig.StatusAsync(RepoRole.Mods)).Branch);
    }

    [Fact(Timeout = 240_000)]
    public async Task A_launch_of_a_profile_that_never_saved_the_mods_folder_leaves_it_as_it_is_and_says_so()
    {
        var rig = await LaunchRig.CreateAsync(gameArguments: ["--exit-after", "800"]);
        using var _ = rig;
        var vanilla = await rig.ProfileAsync("Vanilla");
        var mods = await rig.AddModsFolderAsync("modA/info.xml", "the player's own mod");

        var job = await rig.StartAsync(Tip(vanilla));

        Assert.Equal(JobStatus.Succeeded, job.Status);
        Assert.Equal("the player's own mod", await File.ReadAllTextAsync(Path.Combine(mods, "modA", "info.xml"), rig.Token));
        Assert.Contains(JobLine.ForStep(JobSteps.LaunchFolderNotIncluded, ("folder", RepoRole.Mods)), Steps(job));
    }

    [Fact(Timeout = 240_000)]
    public async Task The_save_after_play_adds_the_mods_folder_to_a_profile_that_never_saved_it()
    {
        var rig = await LaunchRig.CreateAsync(gameArguments: ["--exit-after", "800"]);
        using var _ = rig;
        var vanilla = await rig.ProfileAsync("Vanilla");
        await rig.AddModsFolderAsync("modA/info.xml", "the player's own mod");
        await rig.StartAsync(Tip(vanilla));
        await rig.WaitForAsync<SessionState.Exited>();

        Assert.True((await rig.Launch.CompleteSessionAsync(rig.Game.Id, new SessionCompletion.CaptureNew("After play"))).Ok);
        var done = Assert.IsType<SessionState.Completed>((await rig.WaitForAsync<SessionState.Completed>()).State);

        Assert.Null(done.Error);
        Assert.All(Assert.IsType<CaptureResult>(done.Result).Folders, folder => Assert.True(folder.Ok, folder.Error?.Detail));
        Assert.Equal(["After play"], (await rig.CommitsAsync(RepoRole.Mods, vanilla)).Select(commit => commit.Subject));
    }

    [Fact(Timeout = 240_000)]
    public async Task The_session_counts_the_files_that_changed_in_the_mods_folder_while_playing()
    {
        var (rig, _, modded) = await ThreeFolderRig.WithTwoProfilesAndModsAsync();
        using var _ = rig;
        await rig.StartAsync(Tip(modded));
        await rig.WaitForGameAsync();
        rig.WriteMods("modC/info.xml", "mod c");
        rig.WriteMods("modA/info.xml", "mod a, changed again");
        rig.WriteSave("played.sav", "progress");

        Assert.True((await rig.Launch.MarkFinishedAsync(rig.Game.Id)).Ok);
        var exited = Assert.IsType<SessionState.Exited>((await rig.WaitForAsync<SessionState.Exited>()).State);

        Assert.Equal(2, exited.ChangedModsFileCount);
        Assert.Equal(1, exited.NewFileCount);
        Assert.Equal(0, exited.ChangedInstallFileCount);
    }

    [Fact(Timeout = 240_000)]
    public async Task Saving_after_play_writes_a_point_in_all_three_folders()
    {
        var (rig, _, modded) = await ThreeFolderRig.WithTwoProfilesAndModsAsync();
        using var _ = rig;
        await rig.StartAsync(Tip(modded));
        await rig.WaitForGameAsync();
        rig.WriteMods("modC/info.xml", "mod c");
        rig.WriteSave("played.sav", "progress");
        Assert.True((await rig.Launch.MarkFinishedAsync(rig.Game.Id)).Ok);
        await rig.WaitForAsync<SessionState.Exited>();

        Assert.True((await rig.Launch.CompleteSessionAsync(rig.Game.Id, new SessionCompletion.CaptureNew("After play"))).Ok);
        var done = Assert.IsType<SessionState.Completed>((await rig.WaitForAsync<SessionState.Completed>()).State);

        Assert.Null(done.Error);
        Assert.Equal("After play", (await rig.CommitsAsync(RepoRole.Mods, modded))[0].Subject);
        Assert.Contains("modC/info.xml", await rig.WithEngineAsync((engine, repo) => engine.ListTreeFilesAsync(repo, modded.BranchName, rig.Token), RepoRole.Mods));
    }
}
