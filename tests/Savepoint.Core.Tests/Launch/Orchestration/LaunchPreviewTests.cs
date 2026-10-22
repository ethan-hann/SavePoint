using Savepoint.Core.Api;
using Savepoint.Core.Catalog;

namespace Savepoint.Core.Tests.Launch.Orchestration;

// What a launch would change, read before anything moves: both folders, the saves on offer, and the flags the launch flow branches on.
public class LaunchPreviewTests(TwoProfiles shared) : IClassFixture<TwoProfiles>
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private LaunchRig Shared => shared.Rig;

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
    public async Task A_preview_changes_nothing_on_disk_and_starts_no_job_or_session()
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
