using Savepoint.Core.Api;
using Savepoint.Core.Catalog;

namespace Savepoint.Core.Tests.Launch.Orchestration;

// The saves and history points a launch can load, and the preview of a folder that moved.
public class LaunchPreviewSavesTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // Vanilla is the game as installed; Modded adds two mod files, patches a game file and has a second save. The folders end on Modded.
    private static Task<(LaunchRig Rig, Profile Vanilla, Profile Modded)> TwoProfilesAsync() => LaunchRig.WithTwoProfilesAsync();

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
}
