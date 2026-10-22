using Avalonia.Headless.XUnit;
using Savepoint.App.Features.Launch;

namespace Savepoint.App.Tests.Features.EndToEnd.RealBackend;

// Two profiles of one game with different install content and saves: playing the other one asks first, then rewrites both of the game's
// folders to exactly what that profile saved, and playing the first again rewrites them back.
public class SwitchProfilesJourneyTests
{
    [AvaloniaFact(Timeout = 240_000)]
    public async Task Playing_the_other_profile_rewrites_both_folders_byte_for_byte_and_playing_the_first_again_puts_them_back()
    {
        await using var rig = await JourneyRig.StartAsync();
        var two = await rig.TwoProfilesAsync("Switch Game", "--exit-after", "1500");
        var (game, vanilla, modded) = (two.Game, two.Vanilla, two.Modded);
        Assert.NotEqual(two.VanillaInstall, two.ModdedInstall);
        Assert.NotEqual(two.VanillaSaves, two.ModdedSaves);

        // Playing Vanilla asks first, and says what it will put back.
        await rig.PlayWithSwitchAsync(
            vanilla,
            dialog =>
            {
                Assert.Equal("Switch to \"Vanilla\"?", dialog.Title);
                Assert.True(dialog.HasVersionSection);
                Assert.EndsWith("with 2 mod files, to ", dialog.VersionBefore, StringComparison.Ordinal);
                Assert.EndsWith("with no mods", dialog.VersionTarget, StringComparison.Ordinal);
                Assert.True(dialog.HasSaveSection);
                Assert.Single(dialog.SaveOptions);
                Assert.Empty(dialog.UnsavedBlocks);
                Assert.True(dialog.CanSwitch);
                rig.Render("3a-switch-to-vanilla");
            });
        var afterFirstPlay = await rig.DialogAsync<PostPlayDialogViewModel>();

        // By the time the game has run and closed, both folders hold what Vanilla saved, file for file.
        Assert.Equal(two.VanillaInstall, FolderSnapshot.Of(game.Install));
        Assert.Equal(two.VanillaSaves, FolderSnapshot.Of(game.Saves));
        Assert.False(File.Exists(Path.Combine(game.Install, "mods", "a.txt")));
        Assert.False(File.Exists(Path.Combine(game.Saves, "slot2.sav")));
        await rig.SkipSessionAsync(afterFirstPlay);
        await rig.WaitAsync(() => vanilla.IsActive && !modded.IsActive && vanilla.LastPlayedText != "-", "the rows to show Vanilla as the active profile");

        // And back: playing Modded asks again, and restores its mods, its patched file and its second save.
        await rig.PlayWithSwitchAsync(
            modded,
            dialog =>
            {
                Assert.Equal("Switch to \"Modded\"?", dialog.Title);
                Assert.EndsWith("with no mods, to ", dialog.VersionBefore, StringComparison.Ordinal);
                Assert.EndsWith("with 2 mod files", dialog.VersionTarget, StringComparison.Ordinal);
                Assert.Empty(dialog.UnsavedBlocks);
            });
        var afterSecondPlay = await rig.DialogAsync<PostPlayDialogViewModel>();

        Assert.Equal(two.ModdedInstall, FolderSnapshot.Of(game.Install));
        Assert.Equal(two.ModdedSaves, FolderSnapshot.Of(game.Saves));
        await rig.SkipSessionAsync(afterSecondPlay);
        await rig.WaitAsync(() => modded.IsActive && !vanilla.IsActive, "the rows to show Modded as the active profile again");
    }
}
