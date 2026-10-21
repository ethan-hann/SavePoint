using Savepoint.Core.Api;
using Savepoint.Core.Catalog;

namespace Savepoint.Core.Tests.Launch.Orchestration;

// Throwing away one folder's unsaved changes, the way the launch dialog's "Discard them" does.
public class LaunchDiscardTests
{
    private static LaunchTarget Tip(Profile profile) => new LaunchTarget.ProfileTip(profile.Id);

    [Fact(Timeout = 180_000)]
    public async Task Discarding_returns_one_folder_to_its_last_point_and_is_refused_while_the_game_runs()
    {
        var (rig, _, modded) = await LaunchRig.WithTwoProfilesAsync();
        using var _ = rig;
        rig.WriteSave("slot1.sav", "played on");
        rig.WriteSave("slot3.sav", "a new save");
        rig.WriteInstall("mods/c.txt", "an unsaved mod");

        var saves = await rig.Launch.DiscardChangesAsync(rig.Game.Id, RepoRole.External);

        // Only the folder asked for is touched.
        Assert.True(saves.Ok, saves.Error?.Detail);
        Assert.Equal("save one", rig.ReadSave("slot1.sav"));
        Assert.False(rig.SavesHave("slot3.sav"));
        Assert.True(rig.InstallHas("mods/c.txt"));
        Assert.True((await rig.PreviewAsync(Tip(modded))).Install.Dirty);

        var install = await rig.Launch.DiscardChangesAsync(rig.Game.Id, RepoRole.Install);

        Assert.True(install.Ok, install.Error?.Detail);
        Assert.False(rig.InstallHas("mods/c.txt"));
        var preview = await rig.PreviewAsync(Tip(modded));
        Assert.True(preview.ShowBanner);

        // The files of a running game are not thrown away under it.
        await rig.StartAsync(Tip(modded));
        await rig.WaitForGameAsync();
        rig.WriteSave("slot3.sav", "a new save");

        var refused = await rig.Launch.DiscardChangesAsync(rig.Game.Id, RepoRole.External);

        Assert.Equal("error.gameService.gameRunning", refused.Error?.MessageId);
        Assert.True(rig.SavesHave("slot3.sav"));
    }

    [Fact(Timeout = 90_000)]
    public async Task A_folder_with_no_saved_point_cannot_be_discarded_and_a_missing_saves_folder_is_a_no_op()
    {
        using var rig = await LaunchRig.CreateAsync(withSaves: false);
        rig.WriteInstall("mods/c.txt", "the only copy of this");

        var install = await rig.Launch.DiscardChangesAsync(rig.Game.Id, RepoRole.Install);
        var saves = await rig.Launch.DiscardChangesAsync(rig.Game.Id, RepoRole.External);
        var unknown = await rig.Launch.DiscardChangesAsync("no-such-game", RepoRole.Install);

        Assert.Equal("error.profile.noPoints", install.Error?.MessageId);
        Assert.True(rig.InstallHas("mods/c.txt"));
        Assert.True(saves.Ok, saves.Error?.Detail);
        Assert.Equal("error.game.notFound", unknown.Error?.MessageId);
    }
}
