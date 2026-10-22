using Avalonia.Headless.XUnit;
using Savepoint.App.Features.Common.Errors;
using Savepoint.App.Features.Launch;

namespace Savepoint.App.Tests.Features.EndToEnd.RealBackend;

// Another program holds one of the game's files open while SavePoint switches profiles: the failure names the file, and once the program lets go,
// Try again carries the switch through to the end.
public class FileInUseJourneyTests
{
    [AvaloniaFact(Timeout = 300_000)]
    public async Task A_file_another_program_holds_fails_the_switch_by_name_and_Try_again_finishes_it_once_it_is_released()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Only Windows refuses to replace a file another program holds open.");
        await using var rig = await JourneyRig.StartAsync();
        var two = await rig.TwoProfilesAsync("Locked Game", "--exit-after", "1500");
        var (game, vanilla) = (two.Game, two.Vanilla);

        // The stand-in game, started on its own, holds the patched game file that the switch to Vanilla has to put back.
        var held = Path.Combine(game.Install, "data", "base.pak");
        ErrorCardViewModel failure;
        using (var holder = StandInProcess.Start(Path.Combine(rig.Root, "holder-ready.txt"), "--lock", held))
        {
            await holder.WaitUntilReadyAsync(TestContext.Current.CancellationToken);

            await rig.PlayWithSwitchAsync(vanilla);
            await rig.WaitAsync(() => rig.Games.InlineContent is ErrorCardViewModel, "the failure card");
            failure = (ErrorCardViewModel)rig.Games.InlineContent!;

            // The card says a file is in use, names it, and offers Try again first.
            Assert.Equal("A file is in use, so the switch stopped partway", failure.Title);
            Assert.NotNull(failure.Path);
            Assert.EndsWith("base.pak", failure.Path, StringComparison.Ordinal);
            Assert.Equal(["Try again", "Dismiss"], failure.Actions.Select(action => action.Label));
            Assert.True(failure.Actions[0].IsPrimary);
            Assert.Empty(rig.Main.Dialogs.Stack.OfType<SwitchProgressDialogViewModel>());
            await rig.WaitAsync(() => vanilla.CanPlay, "Play to come back");
            rig.Render("6a-a-file-is-in-use");
        }

        // The other program lets go; Try again picks up where the switch stopped and the game starts.
        await rig.WaitAsync(() => CanWrite(held), "the other program to release the file");
        await rig.FinishesAsync(JourneyRig.RunAsync(failure.Actions[0].Command), "Try again to hand the game over");
        var afterPlay = await rig.DialogAsync<PostPlayDialogViewModel>();
        Assert.Equal(two.VanillaInstall, FolderSnapshot.Of(game.Install));
        Assert.Equal(two.VanillaSaves, FolderSnapshot.Of(game.Saves));
        await rig.SkipSessionAsync(afterPlay);
        await rig.WaitAsync(() => vanilla.IsActive && !two.Modded.IsActive, "the rows to show Vanilla as the active profile");
    }

    private static bool CanWrite(string path)
    {
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
    }
}
