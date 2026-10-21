using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Git;

namespace Savepoint.Core.Tests.Launch.Orchestration;

// Unsaved changes and unfinished switches in the preview: who they belong to and how many there are.
public class LaunchPreviewDirtyTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // Vanilla is the game as installed; Modded adds two mod files, patches a game file and has a second save. The folders end on Modded.
    private static Task<(LaunchRig Rig, Profile Vanilla, Profile Modded)> TwoProfilesAsync() => LaunchRig.WithTwoProfilesAsync();

    [Fact]
    public async Task Unsaved_changes_are_counted_per_folder_and_belong_to_the_profile_the_folder_is_on()
    {
        var (rig, vanilla, modded) = await TwoProfilesAsync();
        using var _ = rig;
        rig.WriteInstall("mods/c.txt", "an unsaved mod");
        rig.WriteSave("slot1.sav", "played on");
        rig.WriteSave("slot3.sav", "a new save");

        var preview = await rig.PreviewAsync(new LaunchTarget.ProfileTip(vanilla.Id));

        Assert.True(preview.Install.Dirty);
        Assert.Equal(1, preview.Install.DirtyFileCount);
        Assert.Equal(modded.Id, preview.Install.DirtyOwnerProfileId);
        Assert.True(preview.External!.Dirty);
        Assert.Equal(2, preview.External.DirtyFileCount);
        Assert.Equal(modded.Id, preview.External.DirtyOwnerProfileId);
        Assert.False(preview.ShowBanner);
        Assert.False(preview.Install.MatchesTarget);
    }

    [Fact]
    public async Task A_dirty_folder_on_the_target_is_still_unsaved_and_does_not_count_as_matching()
    {
        var (rig, _, modded) = await TwoProfilesAsync();
        using var _ = rig;
        rig.WriteSave("slot3.sav", "a new save");

        var preview = await rig.PreviewAsync(new LaunchTarget.ProfileTip(modded.Id));

        Assert.True(preview.Install.MatchesTarget);
        Assert.False(preview.External!.MatchesTarget);
        Assert.True(preview.External.Dirty);
        Assert.Equal(1, preview.External.DirtyFileCount);
        Assert.Equal(modded.Id, preview.External.DirtyOwnerProfileId);
        Assert.False(preview.ShowBanner);
        Assert.False(preview.OnlySaveDiffers);
    }

    [Fact]
    public async Task Unsaved_changes_in_a_folder_left_on_an_older_save_belong_to_no_profile()
    {
        var (rig, _, modded) = await TwoProfilesAsync();
        using var _ = rig;
        var commits = await rig.CommitsAsync(RepoRole.External, modded);
        var oldest = commits[^1].Id;
        await rig.WithEngineAsync((engine, repo) => ReturnAfter(engine.CheckoutCommitAsync(repo, oldest, null, Ct)), RepoRole.External);
        rig.WriteSave("slot9.sav", "played from the old save");

        var preview = await rig.PreviewAsync(new LaunchTarget.ProfileTip(modded.Id));

        Assert.True(preview.External!.Dirty);
        Assert.Equal(1, preview.External.DirtyFileCount);
        Assert.Null(preview.External.DirtyOwnerProfileId);
    }

    [Fact]
    public async Task Unsaved_changes_on_a_branch_whose_profile_is_gone_belong_to_no_profile()
    {
        var (rig, _, modded) = await TwoProfilesAsync();
        using var _ = rig;
        await rig.Workspace.Backend.Services.Catalog.DeleteProfileAsync(modded.Id, Ct);
        var other = (await rig.Workspace.Profiles.ListAsync(rig.Game.Id, Ct)).Value!.Single();
        rig.WriteInstall("mods/c.txt", "an unsaved mod");

        var preview = await rig.PreviewAsync(new LaunchTarget.ProfileTip(other.Id));

        Assert.True(preview.Install.Dirty);
        Assert.Null(preview.Install.DirtyOwnerProfileId);
    }

    [Fact]
    public async Task A_half_finished_switch_to_this_target_is_a_pending_switch_and_not_unsaved_work()
    {
        var (rig, vanilla, _) = await TwoProfilesAsync();
        using var _ = rig;
        await WriteSwitchNoteAsync(rig, RepoRole.Install, toBranchOf: vanilla);
        rig.WriteInstall("mods/a.txt", "half way");

        var preview = await rig.PreviewAsync(new LaunchTarget.ProfileTip(vanilla.Id));

        Assert.False(preview.Install.Dirty);
        Assert.False(preview.Install.MatchesTarget);
        Assert.NotNull(preview.Install.Switch);
        Assert.False(preview.ShowBanner);
    }

    [Fact]
    public async Task A_half_finished_switch_to_another_target_blocks_the_folder_like_unsaved_work()
    {
        var (rig, vanilla, modded) = await TwoProfilesAsync();
        using var _ = rig;
        await WriteSwitchNoteAsync(rig, RepoRole.Install, toBranchOf: modded);

        var preview = await rig.PreviewAsync(new LaunchTarget.ProfileTip(vanilla.Id));

        Assert.True(preview.Install.Dirty);
        Assert.Null(preview.Install.DirtyOwnerProfileId);
    }

    private static async Task<bool> ReturnAfter(Task work)
    {
        await work;
        return true;
    }

    // Leaves the note a stopped checkout leaves: the folder stays as it is and says which point it was switching to.
    private static async Task WriteSwitchNoteAsync(LaunchRig rig, RepoRole role, Profile toBranchOf)
    {
        var target = (await rig.CommitsAsync(role, toBranchOf))[0].Id;
        var status = await rig.StatusAsync(role);
        var gitDir = await rig.WithEngineAsync((_, repo) => Task.FromResult(repo.GitDir), role);
        SwitchJournal.Write(gitDir, status.HeadCommit, target);
    }
}
