using Avalonia.Headless.XUnit;
using Savepoint.App.Features.Common.Dialogs;
using Savepoint.App.Features.Launch;
using Savepoint.Core.Catalog;

namespace Savepoint.App.Tests.Features.EndToEnd.RealBackend;

// Work in the game's folder that nobody saved: playing another profile stops at it and asks, "Save them first" keeps the work in the profile it
// belongs to, and "Discard them" throws it away, but only after a second question that says exactly what goes.
public class UnsavedChangesJourneyTests
{
    [AvaloniaFact(Timeout = 300_000)]
    public async Task Save_them_first_keeps_the_work_in_its_profile_before_the_switch_and_playing_that_profile_again_brings_it_back()
    {
        await using var rig = await JourneyRig.StartAsync();
        var two = await rig.TwoProfilesAsync("Unsaved Keep Game", "--exit-after", "1500");
        var (game, vanilla, modded) = (two.Game, two.Vanilla, two.Modded);
        await EditModdedAsync(rig, game);
        var edited = FolderSnapshot.Of(game.Install);
        Assert.NotEqual(two.ModdedInstall, edited);
        Assert.True(modded.HasUnsavedChanges);
        Assert.StartsWith("Unsaved changes", modded.SecondaryText, StringComparison.Ordinal);

        // Play stops at the unsaved work before anything is switched, and will not go on until it is dealt with.
        var play = vanilla.PlayCommand.ExecuteAsync(null);
        var dialog = await rig.DialogAsync<LaunchDialogViewModel>();
        var block = Assert.Single(dialog.UnsavedBlocks);
        Assert.Equal("You have unsaved changes from your last session", block.Title);
        Assert.Equal(RepoRole.Install, block.Role);
        Assert.True(block.CanSaveFirst, "the changes belong to Modded, so they can be saved into it");
        Assert.False(dialog.ShowKeepCallout);
        Assert.False(dialog.CanSwitch);
        rig.Render("5a-unsaved-changes-before-a-switch");

        // "Save them first" saves them into Modded as a point of its own, and the dialog lets the switch go ahead.
        await rig.FinishesAsync(block.SaveFirstCommand.ExecuteAsync(null), "the changes to be saved first");
        Assert.Empty(dialog.UnsavedBlocks);
        Assert.True(dialog.CanSwitch);
        var moddedPoints = (await rig.Services.History.GetAsync(two.GameId, modded.Id, TestContext.Current.CancellationToken)).Value!.Points;
        Assert.Contains("Before switching to Vanilla", moddedPoints.Select(point => point.Name));
        Assert.Equal(edited, FolderSnapshot.Of(game.Install));

        dialog.SwitchCommand.Execute(null);
        await rig.FinishesAsync(play, "Play to hand the game over");
        var afterVanilla = await rig.DialogAsync<PostPlayDialogViewModel>();
        Assert.Equal(two.VanillaInstall, FolderSnapshot.Of(game.Install));
        Assert.Equal(two.VanillaSaves, FolderSnapshot.Of(game.Saves));
        await rig.SkipSessionAsync(afterVanilla);

        // The saved work is part of Modded now: switching back restores it, edits and all.
        await rig.PlayWithSwitchAsync(modded, back => Assert.Empty(back.UnsavedBlocks));
        var afterModded = await rig.DialogAsync<PostPlayDialogViewModel>();
        Assert.Equal(edited, FolderSnapshot.Of(game.Install));
        await rig.SkipSessionAsync(afterModded);
    }

    [AvaloniaFact(Timeout = 300_000)]
    public async Task Discard_them_asks_again_naming_what_goes_and_only_a_yes_puts_the_folder_back_as_it_was_saved()
    {
        await using var rig = await JourneyRig.StartAsync();
        var two = await rig.TwoProfilesAsync("Unsaved Discard Game", "--exit-after", "1500");
        var (game, vanilla) = (two.Game, two.Vanilla);
        await EditModdedAsync(rig, game);
        var edited = FolderSnapshot.Of(game.Install);

        var play = vanilla.PlayCommand.ExecuteAsync(null);
        var dialog = await rig.DialogAsync<LaunchDialogViewModel>();
        var block = Assert.Single(dialog.UnsavedBlocks);
        Assert.Equal(1, block.RemovedFileCount);
        Assert.Equal(1, block.RestoredFileCount);

        // The second question names the folder, and counts the files it deletes and the files it puts back.
        var discardAsked = block.DiscardCommand.ExecuteAsync(null);
        var question = await rig.DialogAsync<ConfirmDialogViewModel>();
        Assert.Equal("Discard these changes?", question.Title);
        Assert.Contains(game.Install, question.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("1 new file is deleted.", question.Message, StringComparison.Ordinal);
        Assert.Contains("1 changed file is put back.", question.Message, StringComparison.Ordinal);
        Assert.Contains("This can't be undone.", question.Message, StringComparison.Ordinal);
        Assert.Equal("Discard them, permanently", question.ConfirmLabel);
        Assert.True(question.IsDestructive);
        rig.Render("5b-discard-asks-again");

        // Saying no changes nothing.
        question.CancelCommand.Execute(null);
        await rig.FinishesAsync(discardAsked, "the question to be answered");
        Assert.Single(dialog.UnsavedBlocks);
        Assert.Equal(edited, FolderSnapshot.Of(game.Install));

        // Saying yes deletes the new file and puts the changed one back, and the switch can go ahead.
        var discard = block.DiscardCommand.ExecuteAsync(null);
        (await rig.DialogAsync<ConfirmDialogViewModel>()).ConfirmCommand.Execute(null);
        await rig.FinishesAsync(discard, "the discard to finish");
        Assert.Empty(dialog.UnsavedBlocks);
        Assert.True(dialog.CanSwitch);
        Assert.Equal(two.ModdedInstall, FolderSnapshot.Of(game.Install));
        Assert.False(File.Exists(Path.Combine(game.Install, "mods", "c.txt")));

        dialog.SwitchCommand.Execute(null);
        await rig.FinishesAsync(play, "Play to hand the game over");
        var afterVanilla = await rig.DialogAsync<PostPlayDialogViewModel>();
        Assert.Equal(two.VanillaInstall, FolderSnapshot.Of(game.Install));
        await rig.SkipSessionAsync(afterVanilla);
    }

    // Work done after Modded was saved: a new mod file and a changed game file. The Games screen reads the folders again, as it does whenever it is shown.
    private static async Task EditModdedAsync(JourneyRig rig, GameSetup game)
    {
        JourneyRig.WriteFile(Path.Combine(game.Install, "mods", "c.txt"), "work nobody saved");
        JourneyRig.WriteFile(Path.Combine(game.Install, "data", "base.pak"), "patched again, after the last save");
        await rig.Games.ReloadAsync();
    }
}
