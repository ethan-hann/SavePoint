using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Launch.Orchestration;
using Savepoint.Core.Services;

namespace Savepoint.Core.Tests.Launch.Orchestration;

// A switch that stopped part way is a state of its own: the preview says so and names a target that finishes it (D31).
public class LaunchUnfinishedSwitchTests
{
    private static readonly TimeSpan _held = TimeSpan.FromSeconds(20);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static LaunchTarget Tip(Profile profile) => new LaunchTarget.ProfileTip(profile.Id);

    // Starts a switch to Vanilla and cancels it before git has written anything, which leaves the install folder marked unfinished.
    private static async Task CancelSwitchToAsync(LaunchRig rig, Profile profile)
    {
        using var reached = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        // The first question is the check made before any folder is switched; the switch itself asks second, and that is where it is held.
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

    [Fact(Timeout = 150_000)]
    public async Task A_preview_for_another_target_reports_the_unfinished_switch_with_the_target_that_finishes_it()
    {
        var (rig, vanilla, modded) = await LaunchRig.WithTwoProfilesAsync();
        using var _ = rig;
        await CancelSwitchToAsync(rig, vanilla);

        var preview = await rig.PreviewAsync(Tip(modded));

        Assert.True(preview.Install.UnfinishedSwitch);
        Assert.True(preview.Install.Dirty);
        Assert.Null(preview.Install.DirtyOwnerProfileId);
        Assert.Equal(Tip(vanilla), preview.Install.UnfinishedSwitchTarget);
        Assert.False(preview.External!.UnfinishedSwitch);
        Assert.Null(preview.External.UnfinishedSwitchTarget);
    }

    [Fact(Timeout = 150_000)]
    public async Task Starting_the_reported_target_finishes_the_switch()
    {
        var (rig, vanilla, modded) = await LaunchRig.WithTwoProfilesAsync();
        using var _ = rig;
        await CancelSwitchToAsync(rig, vanilla);
        var target = (await rig.PreviewAsync(Tip(modded))).Install.UnfinishedSwitchTarget!;

        var finished = await rig.StartAsync(target);

        Assert.Equal(JobStatus.Succeeded, finished.Status);
        var install = await rig.StatusAsync(RepoRole.Install);
        Assert.Null(install.InterruptedSwitch);
        Assert.Equal(vanilla.BranchName, install.Branch);
        Assert.False(rig.InstallHas("mods/a.txt"));
    }

    [Fact(Timeout = 150_000)]
    public async Task The_preview_of_the_very_switch_that_was_stopped_is_not_dirty_and_still_reports_the_state()
    {
        var (rig, vanilla, _) = await LaunchRig.WithTwoProfilesAsync();
        using var _ = rig;
        await CancelSwitchToAsync(rig, vanilla);

        var preview = await rig.PreviewAsync(Tip(vanilla));

        Assert.True(preview.Install.UnfinishedSwitch);
        Assert.False(preview.Install.Dirty);
        Assert.Equal(Tip(vanilla), preview.Install.UnfinishedSwitchTarget);
    }


    [Fact(Timeout = 150_000)]
    public async Task The_target_that_finishes_the_switch_is_the_profile_it_was_heading_for_even_when_another_profile_holds_the_same_point()
    {
        var (rig, vanilla, modded) = await LaunchRig.WithTwoProfilesAsync();
        using var _ = rig;

        // A duplicate shares its source's install point, so the point alone cannot say which of the two the switch was for.
        var copy = (await rig.Profiles.DuplicateAsync(rig.Game.Id, vanilla.Id, "Copy")).Value!;
        await CancelSwitchToAsync(rig, copy);

        var preview = await rig.PreviewAsync(Tip(modded));

        Assert.Equal(Tip(copy), preview.Install.UnfinishedSwitchTarget);
    }

    [Fact(Timeout = 150_000)]
    public async Task A_point_that_two_profiles_hold_names_no_profile_unless_told_which_branch_was_meant()
    {
        var (rig, vanilla, _) = await LaunchRig.WithTwoProfilesAsync();
        using var _ = rig;
        var copy = (await rig.Profiles.DuplicateAsync(rig.Game.Id, vanilla.Id, "Copy")).Value!;
        var profiles = (await rig.Profiles.ListAsync(rig.Game.Id, Ct)).Value!;

        var (shared, told, uniqueOwner) = await rig.Workspace.Backend.Services.Storage.ReadAsync(async storage =>
        {
            var install = GameFolders.Repos(rig.Workspace.Backend.Services, storage, rig.Game).Single(repo => repo.Role == RepoRole.Install);
            var tip = (await storage.Engine.GetBranchTipAsync(install, vanilla.BranchName, Ct))!;
            var modded = profiles.Single(profile => profile.Name == "Modded");
            var moddedTip = (await storage.Engine.GetBranchTipAsync(install, modded.BranchName, Ct))!;
            return (
                await TargetResolver.TargetOfCommitAsync(storage, install, profiles, tip, null, Ct),
                await TargetResolver.TargetOfCommitAsync(storage, install, profiles, tip, copy.BranchName, Ct),
                await TargetResolver.TargetOfCommitAsync(storage, install, profiles, moddedTip, null, Ct));
        }, Ct);

        Assert.Null(shared);
        Assert.Equal(Tip(copy), told);
        Assert.Equal(new LaunchTarget.ProfileTip(profiles.Single(profile => profile.Name == "Modded").Id), uniqueOwner);
    }


    [Fact(Timeout = 150_000)]
    public async Task A_half_switched_folder_in_which_git_lists_no_file_reports_no_files_not_one()
    {
        var (rig, vanilla, modded) = await LaunchRig.WithTwoProfilesAsync();
        using var _ = rig;
        await CancelSwitchToAsync(rig, vanilla);

        var preview = await rig.PreviewAsync(Tip(modded));

        // The switch was stopped before git wrote anything, so there is nothing to count; a made-up "1" would be read out as a changed file.
        Assert.True(preview.Install.Dirty);
        Assert.Equal(0, preview.Install.DirtyFileCount);
        Assert.Equal(0, preview.Install.DirtyNewFileCount);
    }

    [Fact(Timeout = 150_000)]
    public async Task A_folder_without_an_unfinished_switch_reports_none()
    {
        var (rig, _, modded) = await LaunchRig.WithTwoProfilesAsync();
        using var _ = rig;

        var preview = await rig.PreviewAsync(Tip(modded));

        Assert.False(preview.Install.UnfinishedSwitch);
        Assert.Null(preview.Install.UnfinishedSwitchTarget);
    }
}
