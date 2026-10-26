using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Git;

namespace Savepoint.Core.Tests.Launch.Orchestration;

// What finishing a stopped switch would run into is said in the preview: unsaved changes it would run over come with the way out
// (discard), and files it replaces are counted, and named for the saves folder.
public class LaunchStoppedSwitchReviewTests
{
    private static readonly TimeSpan _held = TimeSpan.FromSeconds(20);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static LaunchTarget Tip(Profile profile) => new LaunchTarget.ProfileTip(profile.Id);

    // Starts a switch to the profile and cancels it before git has written anything, which leaves the install folder marked unfinished.
    private static async Task CancelSwitchToAsync(LaunchRig rig, Profile profile)
    {
        using var reached = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        rig.Workspace.Disk.FreePerPath = path =>
        {
            if (path.StartsWith(rig.InstallPath, StringComparison.OrdinalIgnoreCase) && !release.IsSet)
            {
                reached.Set();
                release.Wait(_held);
            }

            return null;
        };
        try
        {
            var started = await rig.Launch.StartAsync(rig.Game.Id, Tip(profile));
            Assert.True(started.Ok, started.Error?.Detail);
            Assert.True(reached.Wait(_held, Ct), "the switch never reached the engine");
            await rig.Jobs.CancelAsync(started.Value.JobId);
            release.Set();
            await rig.WaitForJobAsync(started.Value.JobId);
        }
        finally
        {
            release.Set();
            rig.Workspace.Disk.FreePerPath = null;
        }

        Assert.NotNull((await rig.StatusAsync(RepoRole.Install)).InterruptedSwitch);
    }

    // The note a switch to the profile leaves in the folder when it is killed before it writes a file.
    private static async Task StopSwitchAsync(LaunchRig rig, RepoRole role, Profile profile)
    {
        var to = (await rig.CommitsAsync(role, profile))[0].Id;
        var from = (await rig.StatusAsync(role)).HeadCommit;
        await rig.WithEngineAsync((_, repo) =>
        {
            SwitchJournal.Write(repo.GitDir, from, to, profile.BranchName);
            return Task.FromResult(0);
        }, role);
    }

    [Fact(Timeout = 150_000)]
    public async Task An_unsaved_change_the_finish_would_run_over_makes_the_folder_dirty_and_takes_the_finish_off_offer()
    {
        var (rig, vanilla, _) = await LaunchRig.WithTwoProfilesAsync();
        using var _ = rig;
        await CancelSwitchToAsync(rig, vanilla);
        rig.WriteInstall("my-notes.txt", "written after the switch stopped");

        var preview = await rig.PreviewAsync(Tip(vanilla));

        Assert.True(preview.Install.UnfinishedSwitch);
        Assert.True(preview.Install.Dirty);
        Assert.Null(preview.Install.UnfinishedSwitchTarget);
        Assert.Equal(1, preview.Install.ChangesOutsideSwitchCount);
        Assert.Equal(1, preview.Install.DirtyFileCount);
        Assert.Equal(1, preview.Install.DirtyNewFileCount);
        Assert.Null(preview.Install.DirtyOwnerProfileId);
    }

    [Fact(Timeout = 150_000)]
    public async Task Starting_is_refused_before_anything_is_written_while_such_a_change_is_there()
    {
        var (rig, vanilla, _) = await LaunchRig.WithTwoProfilesAsync();
        using var _ = rig;
        await CancelSwitchToAsync(rig, vanilla);
        rig.WriteInstall("my-notes.txt", "written after the switch stopped");
        var before = rig.SnapshotOf(RepoRole.Install);

        var job = await rig.StartAsync(Tip(vanilla));

        Assert.Equal(JobStatus.Failed, job.Status);
        Assert.Equal("error.launch.dirtyWorkingTree", job.Error?.MessageId);
        Assert.Equal(before, rig.SnapshotOf(RepoRole.Install));
        Assert.NotNull((await rig.StatusAsync(RepoRole.Install)).InterruptedSwitch);
    }

    [Fact(Timeout = 150_000)]
    public async Task Discarding_the_changes_clears_the_way_and_the_same_switch_then_finishes()
    {
        var (rig, vanilla, _) = await LaunchRig.WithTwoProfilesAsync();
        using var _ = rig;
        await CancelSwitchToAsync(rig, vanilla);
        rig.WriteInstall("my-notes.txt", "written after the switch stopped");
        Assert.True((await rig.PreviewAsync(Tip(vanilla))).Install.Dirty);

        var discarded = await rig.Launch.DiscardChangesAsync(rig.Game.Id, RepoRole.Install);
        Assert.True(discarded.Ok, discarded.Error?.Detail);

        var after = await rig.PreviewAsync(Tip(vanilla));
        Assert.False(after.Install.Dirty);
        Assert.False(after.Install.UnfinishedSwitch);
        Assert.Equal(0, after.Install.ChangesOutsideSwitchCount);
        var finished = await rig.StartAsync(Tip(vanilla));
        Assert.Equal(JobStatus.Succeeded, finished.Status);
        var install = await rig.StatusAsync(RepoRole.Install);
        Assert.Null(install.InterruptedSwitch);
        Assert.Equal(vanilla.BranchName, install.Branch);
        Assert.False(rig.InstallHas("my-notes.txt"));
        Assert.False(rig.InstallHas("mods/a.txt"));
    }

    [Fact(Timeout = 150_000)]
    public async Task A_stopped_switch_with_nothing_in_the_way_stays_clean_and_replaces_nothing()
    {
        var (rig, vanilla, _) = await LaunchRig.WithTwoProfilesAsync();
        using var _ = rig;
        await CancelSwitchToAsync(rig, vanilla);

        var preview = await rig.PreviewAsync(Tip(vanilla));

        Assert.False(preview.Install.Dirty);
        Assert.Equal(Tip(vanilla), preview.Install.UnfinishedSwitchTarget);
        Assert.Equal(0, preview.Install.ChangesOutsideSwitchCount);
        Assert.Equal(0, preview.Install.ReplacedFileCount);
        Assert.Null(preview.Install.ReplacedFileNames);
    }

    [Fact(Timeout = 150_000)]
    public async Task A_file_the_switch_was_changing_that_holds_neither_snapshot_is_counted_for_the_game_folder_without_names()
    {
        var (rig, vanilla, _) = await LaunchRig.WithTwoProfilesAsync();
        using var _ = rig;
        await CancelSwitchToAsync(rig, vanilla);
        rig.WriteInstall("data/base.pak", "the player's own patch, made after the switch stopped");

        var preview = await rig.PreviewAsync(Tip(vanilla));

        Assert.False(preview.Install.Dirty);
        Assert.Equal(Tip(vanilla), preview.Install.UnfinishedSwitchTarget);
        Assert.Equal(1, preview.Install.ReplacedFileCount);
        Assert.Null(preview.Install.ReplacedFileNames);
    }

    [Fact(Timeout = 150_000)]
    public async Task The_saves_folder_names_the_files_the_finish_replaces_up_to_five_and_counts_the_rest()
    {
        using var rig = await LaunchRig.CreateAsync();
        for (var slot = 1; slot <= 7; slot++)
        {
            rig.WriteSave($"slot{slot}.sav", $"vanilla save {slot}");
        }

        var vanilla = await rig.ProfileAsync("Vanilla");
        for (var slot = 1; slot <= 7; slot++)
        {
            rig.WriteSave($"slot{slot}.sav", $"modded save {slot}, later");
        }

        var modded = await rig.ProfileAsync("Modded");
        await StopSwitchAsync(rig, RepoRole.External, vanilla);
        for (var slot = 1; slot <= 7; slot++)
        {
            rig.WriteSave($"slot{slot}.sav", $"neither {slot}");
        }

        var preview = await rig.PreviewAsync(Tip(vanilla));

        Assert.False(preview.External!.Dirty);
        Assert.Equal(7, preview.External.ReplacedFileCount);
        Assert.Equal(["slot1.sav", "slot2.sav", "slot3.sav", "slot4.sav", "slot5.sav"], preview.External.ReplacedFileNames);
        Assert.Equal(modded.BranchName, (await rig.StatusAsync(RepoRole.External)).Branch);
    }

    [Fact(Timeout = 150_000)]
    public async Task The_saves_folder_names_every_file_when_there_are_five_or_fewer()
    {
        using var rig = await LaunchRig.CreateAsync();
        rig.WriteSave("slot1.sav", "vanilla save");
        var vanilla = await rig.ProfileAsync("Vanilla");
        rig.WriteSave("slot1.sav", "modded save, later");
        await rig.ProfileAsync("Modded");
        await StopSwitchAsync(rig, RepoRole.External, vanilla);
        rig.WriteSave("slot1.sav", "neither");

        var preview = await rig.PreviewAsync(Tip(vanilla));

        Assert.Equal(1, preview.External!.ReplacedFileCount);
        Assert.Equal(["slot1.sav"], preview.External.ReplacedFileNames);
    }
}
