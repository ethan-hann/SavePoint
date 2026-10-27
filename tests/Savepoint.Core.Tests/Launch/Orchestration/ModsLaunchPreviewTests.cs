using Savepoint.Core.Api;
using Savepoint.Core.Catalog;

namespace Savepoint.Core.Tests.Launch.Orchestration;

// What the launch dialog is told about the mods folder, and what stops a launch before it writes anything.
public class ModsLaunchPreviewTests
{
    private static readonly TimeSpan _held = TimeSpan.FromSeconds(20);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static LaunchTarget Tip(Profile profile) => new LaunchTarget.ProfileTip(profile.Id);

    [Fact(Timeout = 240_000)]
    public async Task The_preview_has_a_state_for_the_mods_folder_with_what_a_switch_would_change_in_it()
    {
        var (rig, vanilla, _) = await ThreeFolderRig.WithTwoProfilesAndModsAsync();
        using var _ = rig;

        var preview = await rig.PreviewAsync(Tip(vanilla));

        var mods = Assert.IsType<FolderLaunchState>(preview.Folder(RepoRole.Mods));
        Assert.Equal(RepoRole.Mods, mods.Role);
        Assert.Equal(rig.ModsPath(), mods.Path);
        Assert.False(mods.MatchesTarget);
        Assert.False(mods.Dirty);
        Assert.Equal(2, mods.Switch!.FilesChanged);
        Assert.Equal(2, mods.Switch.ModFilesChanged);
        Assert.False(preview.ShowBanner);
    }

    [Fact(Timeout = 240_000)]
    public async Task A_game_without_a_mods_folder_has_no_mods_state()
    {
        var (rig, vanilla, _) = await LaunchRig.WithTwoProfilesAsync();
        using var _ = rig;

        Assert.Null((await rig.PreviewAsync(Tip(vanilla))).Folder(RepoRole.Mods));
    }

    [Fact(Timeout = 240_000)]
    public async Task The_mod_file_count_adds_the_mods_folder_to_the_install_files_the_hints_name()
    {
        var (rig, vanilla, _) = await ThreeFolderRig.WithTwoProfilesAndModsAsync();
        using var _ = rig;

        var preview = await rig.PreviewAsync(Tip(vanilla));

        // Modded holds two install mod files and two in the mods folder; Vanilla holds none in the install and one in the mods folder.
        Assert.Equal(4, preview.CurrentInstall.ModFileCount);
        Assert.Equal(1, preview.TargetInstall.ModFileCount);
    }

    [Fact(Timeout = 240_000)]
    public async Task A_game_that_names_no_hints_still_counts_its_mods_folder()
    {
        var (rig, vanilla, _) = await ThreeFolderRig.WithTwoProfilesAndModsAsync();
        using var _ = rig;
        await rig.EditGameAsync(game => game with { ModPathHints = [] });

        var preview = await rig.PreviewAsync(Tip(vanilla));

        Assert.Equal(2, preview.CurrentInstall.ModFileCount);
        Assert.Equal(1, preview.TargetInstall.ModFileCount);
    }

    [Fact(Timeout = 240_000)]
    public async Task Unsaved_changes_in_the_mods_folder_show_in_the_preview_and_block_the_launch_before_anything_is_switched()
    {
        var (rig, vanilla, modded) = await ThreeFolderRig.WithTwoProfilesAndModsAsync();
        using var _ = rig;
        rig.WriteMods("modC/info.xml", "mod c, not saved");
        var installBefore = rig.SnapshotOf(RepoRole.Install);
        var savesBefore = rig.SnapshotOf(RepoRole.External);
        var modsBefore = rig.ModsSnapshot();

        var preview = await rig.PreviewAsync(Tip(vanilla));
        var blocked = await rig.StartAsync(Tip(vanilla));

        var mods = Assert.IsType<FolderLaunchState>(preview.Folder(RepoRole.Mods));
        Assert.True(mods.Dirty);
        Assert.Equal(1, mods.DirtyFileCount);
        Assert.Equal(1, mods.DirtyNewFileCount);
        Assert.Equal(modded.Id, mods.DirtyOwnerProfileId);
        Assert.False(preview.Install.Dirty);
        Assert.Equal(JobStatus.Failed, blocked.Status);
        Assert.Equal("error.launch.dirtyWorkingTree", blocked.Error?.MessageId);
        Assert.Equal("mods", blocked.Error!.Args["role"]);
        Assert.Equal(installBefore, rig.SnapshotOf(RepoRole.Install));
        Assert.Equal(savesBefore, rig.SnapshotOf(RepoRole.External));
        Assert.Equal(modsBefore, rig.ModsSnapshot());
    }

    [Fact(Timeout = 240_000)]
    public async Task A_mods_folder_that_is_short_of_space_stops_the_launch_before_the_install_folder_is_switched()
    {
        var (rig, vanilla, modded) = await ThreeFolderRig.WithTwoProfilesAndModsAsync();
        using var _ = rig;
        var installBefore = rig.SnapshotOf(RepoRole.Install);
        var savesBefore = rig.SnapshotOf(RepoRole.External);
        var modsBefore = rig.ModsSnapshot();
        rig.Workspace.Disk.FreePerPath = path => path.StartsWith(rig.ModsPath(), StringComparison.OrdinalIgnoreCase) ? 10 : null;

        var failed = await rig.StartAsync(Tip(vanilla));

        Assert.Equal(JobStatus.Failed, failed.Status);
        Assert.Equal("error.diskSpace.low", failed.Error?.MessageId);
        Assert.Equal(installBefore, rig.SnapshotOf(RepoRole.Install));
        Assert.Equal(savesBefore, rig.SnapshotOf(RepoRole.External));
        Assert.Equal(modsBefore, rig.ModsSnapshot());
        var install = await rig.StatusAsync(RepoRole.Install);
        Assert.Equal(modded.BranchName, install.Branch);
        Assert.Null(install.InterruptedSwitch);
        Assert.Null((await rig.StatusAsync(RepoRole.Mods)).InterruptedSwitch);
        Assert.Empty(rig.GameProcessIds());
    }

    [Fact(Timeout = 240_000)]
    public async Task Only_the_saves_differing_is_still_the_skippable_case_and_the_mods_differing_is_not()
    {
        var (rig, _, modded) = await ThreeFolderRig.WithTwoProfilesAndModsAsync();
        using var _ = rig;
        var first = (await rig.History.GetAsync(rig.Game.Id, modded.Id, rig.Token)).Value!.Points.Single(point => point.Name == "Modded");

        // Later points that touch only the saves: going back to the first one changes the saves folder and nothing else.
        rig.WriteSave("slot3.sav", "third save");
        await rig.CaptureAsync(modded, "More saves", RepoRole.External);
        var savesOnly = await rig.PreviewAsync(new LaunchTarget.Point(first.Id));

        // Then a point that touches the mods folder too: going back now changes the mods folder as well.
        rig.WriteMods("modC/info.xml", "mod c");
        rig.WriteSave("slot4.sav", "fourth save");
        await rig.CaptureAsync(modded, "More mods and saves", RepoRole.Mods, RepoRole.External);
        var modsToo = await rig.PreviewAsync(new LaunchTarget.Point(first.Id));

        Assert.True(savesOnly.Folder(RepoRole.Mods)!.MatchesTarget);
        Assert.True(savesOnly.OnlySaveDiffers);
        Assert.False(modsToo.Folder(RepoRole.Mods)!.MatchesTarget);
        Assert.False(modsToo.OnlySaveDiffers);
    }

    // Starts a switch to the target and cancels it when the mods folder's own switch is about to write, which is after the install
    // folder has already been switched.
    private static async Task CancelSwitchDuringModsAsync(LaunchRig rig, LaunchTarget target)
    {
        using var reached = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        var calls = 0;
        rig.Workspace.Disk.FreePerPath = path =>
        {
            // The first question is the check made before anything is written; the second is the mods folder's own switch.
            if (path.StartsWith(rig.ModsPath(), StringComparison.OrdinalIgnoreCase) && Interlocked.Increment(ref calls) == 2 && !release.IsSet)
            {
                reached.Set();
                release.Wait(_held);
            }

            return null;
        };
        try
        {
            var started = await rig.Launch.SwitchAsync(rig.Game.Id, target);
            Assert.True(started.Ok, started.Error?.Detail);
            Assert.True(reached.Wait(_held, Ct), "the switch never reached the mods folder's own switch");
            await rig.Jobs.CancelAsync(started.Value.JobId);
            release.Set();
            await rig.WaitForJobAsync(started.Value.JobId);
        }
        finally
        {
            release.Set();
            rig.Workspace.Disk.FreePerPath = null;
        }
    }

    [Fact(Timeout = 240_000)]
    public async Task A_switch_stopped_during_the_mods_folder_leaves_the_install_switched_and_the_saves_untouched()
    {
        var (rig, vanilla, modded) = await ThreeFolderRig.WithTwoProfilesAndModsAsync();
        using var _ = rig;
        var savesBefore = rig.SnapshotOf(RepoRole.External);

        await CancelSwitchDuringModsAsync(rig, Tip(vanilla));

        Assert.Equal(vanilla.BranchName, (await rig.StatusAsync(RepoRole.Install)).Branch);
        Assert.False(rig.InstallHas("mods/a.txt"));
        Assert.NotNull((await rig.StatusAsync(RepoRole.Mods)).InterruptedSwitch);
        Assert.Null((await rig.StatusAsync(RepoRole.External)).InterruptedSwitch);
        Assert.Equal(modded.BranchName, (await rig.StatusAsync(RepoRole.External)).Branch);
        Assert.Equal(savesBefore, rig.SnapshotOf(RepoRole.External));
    }

    [Fact(Timeout = 240_000)]
    public async Task The_preview_after_a_stop_during_mods_reports_it_on_the_mods_folder_with_the_target_that_finishes_it()
    {
        var (rig, vanilla, modded) = await ThreeFolderRig.WithTwoProfilesAndModsAsync();
        using var _ = rig;
        await CancelSwitchDuringModsAsync(rig, Tip(vanilla));

        var preview = await rig.PreviewAsync(Tip(modded));

        var mods = Assert.IsType<FolderLaunchState>(preview.Folder(RepoRole.Mods));
        Assert.True(mods.UnfinishedSwitch);
        Assert.True(mods.Dirty);
        Assert.Equal(Tip(vanilla), mods.UnfinishedSwitchTarget);
        Assert.False(preview.Install.UnfinishedSwitch);
        Assert.False(preview.Folder(RepoRole.External)!.UnfinishedSwitch);
    }

    [Fact(Timeout = 240_000)]
    public async Task Starting_the_same_target_again_finishes_the_mods_folder_and_switches_the_saves_leaving_the_install_where_it_is()
    {
        var (rig, vanilla, _) = await ThreeFolderRig.WithTwoProfilesAndModsAsync();
        using var _ = rig;
        await CancelSwitchDuringModsAsync(rig, Tip(vanilla));
        var installAfterStop = rig.SnapshotOf(RepoRole.Install);

        var finished = await rig.StartAsync(Tip(vanilla));

        Assert.Equal(JobStatus.Succeeded, finished.Status);
        var mods = await rig.StatusAsync(RepoRole.Mods);
        Assert.Null(mods.InterruptedSwitch);
        Assert.Equal(vanilla.BranchName, mods.Branch);
        Assert.True(mods.IsClean);
        Assert.Equal("mod a", rig.ReadMods("modA/info.xml"));
        Assert.False(rig.ModsHave("modB/info.xml"));
        Assert.Equal(installAfterStop, rig.SnapshotOf(RepoRole.Install));
        Assert.Equal(vanilla.BranchName, (await rig.StatusAsync(RepoRole.External)).Branch);
        Assert.False(rig.SavesHave("slot2.sav"));
    }

    [Fact(Timeout = 240_000)]
    public async Task A_switch_stopped_during_mods_on_the_way_to_an_older_point_is_finished_by_that_point_not_by_a_save()
    {
        var (rig, _, modded) = await ThreeFolderRig.WithTwoProfilesAndModsAsync();
        using var _ = rig;
        var older = (await rig.History.GetAsync(rig.Game.Id, modded.Id, rig.Token)).Value!.Points.Single(point => point.Name == "Modded");
        rig.WriteInstall("data/later.pak", "a later patch");
        rig.WriteMods("modC/info.xml", "mod c");
        rig.WriteSave("slot3.sav", "third save");
        await rig.CaptureAsync(modded, "Latest");
        await CancelSwitchDuringModsAsync(rig, new LaunchTarget.Point(older.Id));

        var preview = await rig.PreviewAsync(Tip(modded));

        Assert.Equal(new LaunchTarget.Point(older.Id), preview.Folder(RepoRole.Mods)!.UnfinishedSwitchTarget);
    }
}
