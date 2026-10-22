using Avalonia.Headless.XUnit;
using Savepoint.App.Features.Common.Dialogs;
using Savepoint.App.Features.Games;
using Savepoint.App.Features.Launch;
using Savepoint.App.Features.Settings;
using Savepoint.App.Features.Shell;

namespace Savepoint.App.Tests.Features.EndToEnd.RealBackend;

// Settings that touch what SavePoint keeps: moving its storage to another folder and back, and removing everything it keeps.
public class SettingsJourneyTests
{
    [AvaloniaFact(Timeout = 300_000)]
    public async Task Moving_the_storage_to_another_folder_and_back_keeps_every_point_and_the_profiles_still_switch()
    {
        await using var rig = await JourneyRig.StartAsync();
        var two = await rig.TwoProfilesAsync("Moving Game", "--exit-after", "1500");
        var history = await PointsAsync(rig, two.GameId);
        Assert.Equal(["Modded", "Vanilla"], history.Select(point => point.Name).Order());
        var original = rig.StorageRoot;
        var elsewhere = Path.Combine(rig.Root, "moved-storage");
        Assert.NotEmpty(Directory.EnumerateFiles(original, "*", SearchOption.AllDirectories));

        // Out: the data is copied and checked, SavePoint switches over, and the old copy goes.
        rig.Shell.Navigate(NavDestination.Settings);
        await rig.WaitAsync(() => rig.Settings.IsLoaded && SamePath(rig.Settings.Storage.StoragePath, original), "Settings to show where the data is kept");
        await MoveAsync(rig, elsewhere);
        Assert.Equal(elsewhere, (await rig.Services.Settings.GetAsync(TestContext.Current.CancellationToken)).Value!.StorageRoot, PathComparer());
        Assert.NotEmpty(Directory.EnumerateFiles(elsewhere, "*", SearchOption.AllDirectories));
        Assert.Empty(FilesIn(original));
        Assert.Equal(history, await PointsAsync(rig, two.GameId));
        rig.Render("7a-storage-moved");

        // The history is usable from its new home: playing Vanilla puts both folders back to what it saved.
        await SwitchAndCheckAsync(rig, two.Game, two.Vanilla, two.VanillaInstall, two.VanillaSaves);

        // Back: the same again in the other direction.
        rig.Shell.Navigate(NavDestination.Settings);
        await rig.WaitAsync(() => SamePath(rig.Settings.Storage.StoragePath, elsewhere) && !rig.Settings.Storage.IsRelocating, "Settings to show the new folder");
        await MoveAsync(rig, original);
        Assert.Equal(original, (await rig.Services.Settings.GetAsync(TestContext.Current.CancellationToken)).Value!.StorageRoot, PathComparer());
        Assert.NotEmpty(FilesIn(original));
        Assert.Empty(FilesIn(elsewhere));
        Assert.Equal(history, await PointsAsync(rig, two.GameId));
        await SwitchAndCheckAsync(rig, two.Game, two.Modded, two.ModdedInstall, two.ModdedSaves);
    }

    [AvaloniaFact(Timeout = 300_000)]
    public async Task Removing_all_data_asks_for_a_typed_word_returns_the_app_to_first_run_and_leaves_the_game_folders_as_they_were()
    {
        await using var rig = await JourneyRig.StartAsync();
        var two = await rig.TwoProfilesAsync("Cleared Game");
        var game = two.Game;
        Assert.True(File.Exists(game.InstallMarker) && File.Exists(game.SavesMarker), "the folders carry SavePoint's marker while the game is managed");
        Assert.NotEmpty(FilesIn(rig.StorageRoot));

        rig.Shell.Navigate(NavDestination.Settings);
        await rig.WaitAsync(() => rig.Settings.IsLoaded, "Settings to load");
        var removal = rig.Settings.ClearAll.RemoveCommand.ExecuteAsync(null);
        var dialog = await rig.DialogAsync<ClearAllDataDialogViewModel>();

        // The question says what goes and what stays, and will not go ahead until the word is typed.
        Assert.Equal("Remove all SavePoint data?", dialog.Title);
        Assert.Equal("1 game and 2 profiles, with all their snapshots", dialog.Items[0]);
        Assert.Contains(rig.StorageRoot, dialog.Items[1], StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith("Your game folders and everything in them stay exactly as they are.", dialog.Stays, StringComparison.Ordinal);
        Assert.Equal("Type DELETE to confirm.", dialog.Prompt);
        Assert.False(dialog.CanConfirm);
        dialog.TypedText = "DELET";
        Assert.False(dialog.CanConfirm);
        rig.Render("7b-remove-all-data");
        dialog.ConfirmCommand.Execute(null);
        Assert.False(dialog.IsClosed, "the removal does not start without the word");
        dialog.TypedText = dialog.Word;
        Assert.True(dialog.CanConfirm);
        dialog.ConfirmCommand.Execute(null);
        await rig.FinishesAsync(removal, "the removal to be asked for");

        // SavePoint starts over: the app is back at first run, with no game and no history to go back to.
        await rig.WaitAsync(() => rig.Shell.ShowFirstRun && rig.Shell.Games.Count == 0, "the app to return to first run");
        Assert.Equal(rig.StorageRoot, rig.Shell.FirstRun.StoragePath);
        Assert.False(rig.Shell.Nav.Single(item => item.Destination == NavDestination.History).IsEnabled);
        Assert.Empty((await rig.Services.Games.ListAsync(TestContext.Current.CancellationToken)).Value!);
        Assert.Empty((await rig.Services.Profiles.ListAsync(null, TestContext.Current.CancellationToken)).Value!);
        Assert.Empty(FilesIn(rig.StorageRoot));

        // The game's folders hold exactly what they held, except for SavePoint's own marker, which is gone.
        Assert.Equal(two.ModdedInstall, FolderSnapshot.Of(game.Install));
        Assert.Equal(two.ModdedSaves, FolderSnapshot.Of(game.Saves));
        Assert.False(File.Exists(game.InstallMarker));
        Assert.False(File.Exists(game.SavesMarker));
    }

    // Picks a new folder for the data and confirms the move, then waits until Settings shows the data there.
    private static async Task MoveAsync(JourneyRig rig, string destination)
    {
        rig.Dialogs.AnswerFolder(destination);
        var change = rig.Settings.Storage.ChangeCommand.ExecuteAsync(null);
        var question = await rig.DialogAsync<ConfirmDialogViewModel>();
        Assert.Equal("Move your snapshots?", question.Title);
        Assert.Contains(destination, question.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Move snapshots", question.ConfirmLabel);
        question.ConfirmCommand.Execute(null);
        await rig.FinishesAsync(change, "the move to be asked for");
        await rig.WaitAsync(() => SamePath(rig.Settings.Storage.StoragePath, destination) && !rig.Settings.Storage.IsRelocating, "Settings to show the data in its new folder");
    }

    // Plays a profile that is not in place, and checks both folders hold what it saved by the time the game has closed.
    private static async Task SwitchAndCheckAsync(JourneyRig rig, GameSetup game, ProfileRowViewModel profile, IReadOnlyDictionary<string, string> install, IReadOnlyDictionary<string, string> saves)
    {
        rig.Shell.Navigate(NavDestination.Games);
        await rig.WaitAsync(() => rig.Games.Profiles.All(row => row.CanPlay), "Play to be on offer");
        await rig.PlayWithSwitchAsync(profile);
        var afterPlay = await rig.DialogAsync<PostPlayDialogViewModel>();
        Assert.Equal(install, FolderSnapshot.Of(game.Install));
        Assert.Equal(saves, FolderSnapshot.Of(game.Saves));
        await rig.SkipSessionAsync(afterPlay);
    }

    private static async Task<IReadOnlyList<(string Id, string Name)>> PointsAsync(JourneyRig rig, string gameId)
    {
        var history = await rig.Services.History.GetAsync(gameId, null, TestContext.Current.CancellationToken);
        Assert.True(history.Ok, history.Error?.Detail);
        return [.. history.Value.Points.Select(point => (point.Id.Value, point.Name)).Order()];
    }

    private static string[] FilesIn(string folder) =>
        Directory.Exists(folder) ? [.. Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories)] : [];

    private static bool SamePath(string left, string right) =>
        string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static StringComparer PathComparer() => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}
