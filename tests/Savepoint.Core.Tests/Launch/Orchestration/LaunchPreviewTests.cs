using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Git;

namespace Savepoint.Core.Tests.Launch.Orchestration;

// What a launch would change, read before anything moves: both folders, the saves on offer, and the flags the launch flow branches on.
public class LaunchPreviewTests(TwoProfiles shared) : IClassFixture<TwoProfiles>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private LaunchRig Shared => shared.Rig;

    // Vanilla is the game as installed; Modded adds two mod files, patches a game file and has a second save. The folders end on Modded.
    private static Task<(LaunchRig Rig, Profile Vanilla, Profile Modded)> TwoProfilesAsync() => LaunchRig.WithTwoProfilesAsync();

    [Fact]
    public async Task A_profile_that_is_already_in_place_shows_the_banner_and_has_nothing_to_switch()
    {
        var rig = Shared;
        var modded = shared.Modded;

        var preview = await rig.PreviewAsync(new LaunchTarget.ProfileTip(modded.Id));

        Assert.Equal(rig.Game.Id, preview.GameId);
        Assert.Equal(modded.Id, preview.ProfileId);
        Assert.True(preview.ShowBanner);
        Assert.False(preview.OnlySaveDiffers);
        Assert.False(preview.TargetIsOlderPoint);
        foreach (var folder in new[] { preview.Install, preview.External! })
        {
            Assert.True(folder.MatchesTarget);
            Assert.False(folder.Dirty);
            Assert.Equal(0, folder.DirtyFileCount);
            Assert.Null(folder.DirtyOwnerProfileId);
            Assert.Null(folder.Switch);
        }

        Assert.Equal(rig.InstallPath, preview.Install.Path);
        Assert.Equal(rig.SavesPath, preview.External!.Path);
        Assert.Equal(2, preview.CurrentInstall.ModFileCount);
        Assert.Equal(preview.CurrentInstall, preview.TargetInstall);
    }

    [Fact]
    public async Task Another_profile_needs_both_folders_switched_and_the_preview_sizes_each_switch()
    {
        var rig = Shared;
        var vanilla = shared.Vanilla;

        var preview = await rig.PreviewAsync(new LaunchTarget.ProfileTip(vanilla.Id));

        Assert.False(preview.ShowBanner);
        Assert.False(preview.OnlySaveDiffers);
        Assert.False(preview.Install.MatchesTarget);
        Assert.False(preview.External!.MatchesTarget);

        // Two mod files go away and the game file is put back to what it held.
        var install = Assert.IsType<SwitchSummary>(preview.Install.Switch);
        Assert.Equal(3, install.FilesChanged);
        Assert.Equal(2, install.ModFilesChanged);
        Assert.Equal("base content".Length, install.BytesChanged);
        var saves = Assert.IsType<SwitchSummary>(preview.External.Switch);
        Assert.Equal(1, saves.FilesChanged);
        Assert.Equal(0, saves.ModFilesChanged);

        Assert.Equal(2, preview.CurrentInstall.ModFileCount);
        Assert.Equal(0, preview.TargetInstall.ModFileCount);
    }

    [Fact]
    public async Task The_target_profiles_saves_are_listed_newest_first_with_the_newest_marked_and_selected()
    {
        var (rig, _, modded) = await TwoProfilesAsync();
        using var _ = rig;
        await Task.Delay(1100, Ct);
        rig.WriteSave("slot3.sav", "third save");
        var third = await rig.CaptureAsync(modded, "Third save");
        var thirdId = ((CaptureResult)third.Result!).PointId!.Value;

        var preview = await rig.PreviewAsync(new LaunchTarget.ProfileTip(modded.Id));

        // Modded began from Vanilla, so Vanilla's save is still one it can load.
        Assert.Equal(["Third save", "Modded", "Vanilla"], preview.AvailableSaves.Select(save => save.Name));
        Assert.Equal([true, false, false], preview.AvailableSaves.Select(save => save.IsMostRecent));
        Assert.Equal(thirdId, preview.AvailableSaves[0].Id);
        Assert.True(preview.AvailableSaves[0].CapturedAt > preview.AvailableSaves[1].CapturedAt);
        Assert.Equal(thirdId, preview.SelectedSaveId);
    }

    [Fact]
    public async Task A_profile_without_a_saves_folder_has_no_saves_and_no_external_state()
    {
        using var rig = await LaunchRig.CreateAsync(withSaves: false);
        var only = await rig.ProfileAsync("Only");

        var preview = await rig.PreviewAsync(new LaunchTarget.ProfileTip(only.Id));

        Assert.Null(preview.External);
        Assert.Empty(preview.AvailableSaves);
        Assert.Null(preview.SelectedSaveId);
        Assert.True(preview.ShowBanner);
    }

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

    [Fact]
    public async Task A_folder_that_has_moved_fails_the_preview_and_names_it()
    {
        var (rig, vanilla, _) = await TwoProfilesAsync();
        using var _ = rig;
        Directory.Delete(rig.SavesPath, recursive: true);

        var preview = await rig.Launch.GetPreviewAsync(rig.Game.Id, new LaunchTarget.ProfileTip(vanilla.Id), Ct);

        Assert.Equal("error.path.missing", preview.Error?.MessageId);
        Assert.Equal(rig.SavesPath, preview.Error!.Args["path"]);
        Assert.Equal("external", preview.Error.Args["role"]);
    }

    [Fact]
    public async Task A_save_that_is_not_the_newest_is_an_older_point_and_only_the_saves_differ()
    {
        var (rig, _, modded) = await TwoProfilesAsync();
        using var _ = rig;
        await Task.Delay(1100, Ct);
        rig.WriteSave("slot3.sav", "third save");
        await rig.CaptureAsync(modded, "Third save");
        var older = (await rig.PreviewAsync(new LaunchTarget.ProfileTip(modded.Id))).AvailableSaves[1];

        var preview = await rig.PreviewAsync(new LaunchTarget.Save(modded.Id, older.Id));

        Assert.True(preview.TargetIsOlderPoint);
        Assert.True(preview.OnlySaveDiffers);
        Assert.False(preview.ShowBanner);
        Assert.True(preview.Install.MatchesTarget);
        Assert.False(preview.External!.MatchesTarget);
        Assert.Equal(older.Id, preview.SelectedSaveId);
        Assert.Equal(3, preview.AvailableSaves.Count);
    }

    [Fact]
    public async Task The_newest_save_chosen_explicitly_is_not_an_older_point()
    {
        var rig = Shared;
        var modded = shared.Modded;
        var newest = (await rig.PreviewAsync(new LaunchTarget.ProfileTip(modded.Id))).AvailableSaves[0];

        var preview = await rig.PreviewAsync(new LaunchTarget.Save(modded.Id, newest.Id));

        Assert.False(preview.TargetIsOlderPoint);
        Assert.True(preview.ShowBanner);
    }

    [Fact]
    public async Task A_history_point_restores_each_folder_to_its_own_commit_or_the_profiles_latest_before_it()
    {
        using var rig = await LaunchRig.CreateAsync();
        var profile = await rig.ProfileAsync("Vanilla");
        var first = (await rig.CommitsAsync(RepoRole.Install, profile))[0].CaptureId!;
        await Task.Delay(1100, Ct);

        // A version-only point: the install changes, the saves are not part of it.
        rig.WriteInstall("mods/a.txt", "mod a");
        var versionOnly = ((CaptureResult)(await rig.CaptureAsync(profile, "Added a mod", RepoRole.Install)).Result!).PointId!.Value;
        await Task.Delay(1100, Ct);

        // A save-only point after it.
        rig.WriteSave("slot2.sav", "second save");
        await rig.CaptureAsync(profile, "A newer save", RepoRole.External);

        var preview = await rig.PreviewAsync(new LaunchTarget.Point(versionOnly));

        Assert.Equal(profile.Id, preview.ProfileId);
        Assert.True(preview.TargetIsOlderPoint);
        Assert.True(preview.Install.MatchesTarget);
        Assert.Equal(1, preview.CurrentInstall.ModFileCount);
        Assert.False(preview.External!.MatchesTarget);
        Assert.Equal(1, preview.External.Switch!.FilesChanged);
        Assert.Equal(new HistoryPointId(first), preview.SelectedSaveId);
    }

    [Fact]
    public async Task The_first_point_of_a_profile_goes_back_to_both_folders_as_they_were()
    {
        var rig = Shared;
        var vanilla = shared.Vanilla;
        var vanillaPoint = new HistoryPointId((await rig.CommitsAsync(RepoRole.Install, vanilla))[0].CaptureId!);

        var preview = await rig.PreviewAsync(new LaunchTarget.Point(vanillaPoint));

        Assert.Equal(vanilla.Id, preview.ProfileId);
        Assert.False(preview.TargetIsOlderPoint);
        Assert.False(preview.Install.MatchesTarget);
        Assert.Equal(3, preview.Install.Switch!.FilesChanged);
    }

    [Fact]
    public async Task Naming_a_profile_or_a_point_that_does_not_exist_says_so()
    {
        var rig = Shared;
        var vanilla = shared.Vanilla;

        var profile = await rig.Launch.GetPreviewAsync(rig.Game.Id, new LaunchTarget.ProfileTip("no-such-profile"), Ct);
        var save = await rig.Launch.GetPreviewAsync(rig.Game.Id, new LaunchTarget.Save(vanilla.Id, new HistoryPointId("no-such-save")), Ct);
        var point = await rig.Launch.GetPreviewAsync(rig.Game.Id, new LaunchTarget.Point(new HistoryPointId("no-such-point")), Ct);
        var game = await rig.Launch.GetPreviewAsync("no-such-game", new LaunchTarget.ProfileTip(vanilla.Id), Ct);

        Assert.Equal("error.profile.notFound", profile.Error?.MessageId);
        Assert.Equal("error.history.pointNotFound", save.Error?.MessageId);
        Assert.Equal("error.history.pointNotFound", point.Error?.MessageId);
        Assert.Equal("error.game.notFound", game.Error?.MessageId);
    }

    [Fact]
    public async Task A_preview_changes_nothing_on_disk_and_takes_no_lock_a_capture_needs()
    {
        var rig = Shared;
        var vanilla = shared.Vanilla;
        var installBefore = rig.SnapshotOf(RepoRole.Install);
        var savesBefore = rig.SnapshotOf(RepoRole.External);

        await rig.PreviewAsync(new LaunchTarget.ProfileTip(vanilla.Id));

        Assert.Equal(installBefore, rig.SnapshotOf(RepoRole.Install));
        Assert.Equal(savesBefore, rig.SnapshotOf(RepoRole.External));
        Assert.DoesNotContain(rig.Jobs.List(), job => job.Kind == JobKind.Launch);
        Assert.Null(rig.Launch.GetSession(rig.Game.Id));
    }

    [Fact]
    public async Task A_cancelled_read_fails_with_the_cancelled_message()
    {
        var rig = Shared;
        var vanilla = shared.Vanilla;
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var preview = await rig.Launch.GetPreviewAsync(rig.Game.Id, new LaunchTarget.ProfileTip(vanilla.Id), cancelled.Token);

        Assert.Equal("error.operation.cancelled", preview.Error?.MessageId);
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

// Two profiles built once for the tests that only read: Vanilla is the game as installed; Modded adds two mod files, patches a game
// file and has a second save. The folders end on Modded.
public sealed class TwoProfiles : IAsyncLifetime
{
    internal LaunchRig Rig { get; private set; } = null!;

    internal Profile Vanilla { get; private set; } = null!;

    internal Profile Modded { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        Rig = await LaunchRig.CreateAsync();
        (Vanilla, Modded) = await Rig.BuildTwoProfilesAsync();
    }

    public ValueTask DisposeAsync()
    {
        Rig?.Dispose();
        return ValueTask.CompletedTask;
    }
}
