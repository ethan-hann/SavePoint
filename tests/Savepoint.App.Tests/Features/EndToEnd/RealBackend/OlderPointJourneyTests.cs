using Avalonia.Headless.XUnit;
using Savepoint.App.Features.History;
using Savepoint.App.Features.Launch;
using Savepoint.App.Features.Shell;
using Savepoint.Core.Api;

namespace Savepoint.App.Tests.Features.EndToEnd.RealBackend;

// Going back to an older point from History and playing on from it: the progress cannot be added to the end of the profile, so the app offers a
// new profile, and taking it leaves the original profile exactly as it was.
public class OlderPointJourneyTests
{
    [AvaloniaFact(Timeout = 300_000)]
    public async Task Playing_on_from_an_older_point_and_keeping_it_as_a_new_profile_leaves_the_original_profile_as_it_was()
    {
        await using var rig = await JourneyRig.StartAsync();
        var game = rig.NewGame("Older Game");
        JourneyRig.WriteFile(Path.Combine(game.Saves, "slot1.sav"), "the first chapter");
        var added = await rig.AddGameAsync(game, ["--saves", game.Saves, "--exit-after", "2500"]);
        var vanilla = await rig.CreateProfileAsync("Vanilla");
        var savesAtStart = FolderSnapshot.Of(game.Saves);
        var installAtStart = FolderSnapshot.Of(game.Install);
        await rig.WaitAsync(() => vanilla.IsActive, "the profile to show as active");

        // A first session adds a chapter, and the person keeps it as a second point of Vanilla.
        await rig.FinishesAsync(vanilla.PlayCommand.ExecuteAsync(null), "Play to hand the game over");
        await rig.WaitForRunningAsync();
        JourneyRig.WriteFile(Path.Combine(game.Saves, "chapter2.sav"), "the second chapter");
        var keepSession = await rig.DialogAsync<PostPlayDialogViewModel>();
        rig.Main.Toasts.DismissAll();
        keepSession.SelectedChoice.Name = "Chapter two";
        await rig.FinishesAsync(keepSession.SaveCommand.ExecuteAsync(null), "the after-play dialog to answer");
        await rig.WaitAsync(() => rig.ToastTexts.Contains("Saved Older Game.") && vanilla.CanPlay && rig.Games.InlineContent is null, "the session to be saved");
        var savesAtChapterTwo = FolderSnapshot.Of(game.Saves);
        Assert.Contains("chapter2.sav", savesAtChapterTwo.Keys);
        var originalPoints = await PointsOfAsync(rig, added.Id, vanilla.Id);
        Assert.Equal(["Chapter two", "Vanilla"], originalPoints.Select(point => point.Name));

        // History offers every point; going back to the first one asks first, and says what playing on from it means.
        rig.Shell.Navigate(NavDestination.History);
        await rig.WaitAsync(() => rig.History.State == HistoryState.Loaded && rig.History.Saves.Count == 2, "History to list both points");
        var oldest = rig.History.Groups.SelectMany(group => group.Cards).Single(card => card.Name == "Vanilla");
        Assert.True(oldest.CanGoBack);
        rig.Render("4a-history-with-an-older-point");
        var goBack = JourneyRig.RunAsync(oldest.GoBackCommand);
        var dialog = await rig.DialogAsync<LaunchDialogViewModel>();
        Assert.True(dialog.IsOlderPoint);
        Assert.Contains("older snapshot of \"Vanilla\"", dialog.OlderPointText, StringComparison.Ordinal);
        Assert.False(dialog.HasVersionSection, "the game's own files are the same at both points");
        Assert.Empty(dialog.UnsavedBlocks);
        rig.Render("4b-going-back-to-the-older-point");
        dialog.SwitchCommand.Execute(null);
        await rig.FinishesAsync(goBack, "Go back to this to hand the game over");

        // The launch ends on the Games screen, and the saves folder holds the older point's saves, not the later chapter.
        await rig.WaitForRunningAsync();
        Assert.Equal(NavDestination.Games, rig.Shell.Destination);
        Assert.Equal(savesAtStart, FolderSnapshot.Of(game.Saves));
        Assert.Equal(installAtStart, FolderSnapshot.Of(game.Install));

        // Playing on from there writes a different second chapter. The after-play choices now start with a new profile, not a new point.
        JourneyRig.WriteFile(Path.Combine(game.Saves, "chapter2-alt.sav"), "a different second chapter");
        var afterPlay = await rig.DialogAsync<PostPlayDialogViewModel>();
        Assert.Equal([CompletionChoice.ForkNewProfile, CompletionChoice.MoveForward, CompletionChoice.Skip], afterPlay.Choices.Select(choice => choice.Kind));
        Assert.Equal(CompletionChoice.ForkNewProfile, afterPlay.SelectedChoice.Kind);
        Assert.Equal("Keep this session as a new profile", afterPlay.SelectedChoice.Title);
        Assert.Contains("can't simply go on the end", afterPlay.OlderNote, StringComparison.Ordinal);
        Assert.Equal("Keep it", afterPlay.PrimaryLabel);
        rig.Render("4c-after-playing-on-from-the-older-point");
        afterPlay.SelectedChoice.Name = "Alternate chapter two";
        await rig.FinishesAsync(afterPlay.SaveCommand.ExecuteAsync(null), "the after-play dialog to answer");
        await rig.WaitAsync(() => rig.Games.Profiles.Any(row => row.Name == "Alternate chapter two") && rig.Games.InlineContent is null && rig.Games.Profiles.All(row => row.CanPlay), "the new profile to appear and Play to come back");

        // The new profile holds what was played, and is the one the folders are on. Vanilla still has the same two points, untouched.
        var alternate = rig.Games.Profiles.Single(row => row.Name == "Alternate chapter two");
        await rig.WaitAsync(() => alternate.IsActive && !vanilla.IsActive, "the rows to show the new profile as the active one");
        Assert.Equal(["chapter2-alt.sav", "slot1.sav"], FolderSnapshot.Of(game.Saves).Keys);
        Assert.Equal(originalPoints, await PointsOfAsync(rig, added.Id, vanilla.Id));
        var alternatePoints = await PointsOfAsync(rig, added.Id, alternate.Id);
        Assert.Contains("Alternate chapter two", alternatePoints.Select(point => point.Name));
        rig.Render("4d-the-new-profile-beside-the-original");

        // Playing Vanilla again restores chapter two exactly as it was saved, and none of the alternate one.
        await rig.PlayWithSwitchAsync(vanilla);
        var lastSession = await rig.DialogAsync<PostPlayDialogViewModel>();
        Assert.Equal(savesAtChapterTwo, FolderSnapshot.Of(game.Saves));
        Assert.Equal(installAtStart, FolderSnapshot.Of(game.Install));
        await rig.SkipSessionAsync(lastSession);
    }

    // What a profile's history lists: each point's id and name, newest first.
    private static async Task<IReadOnlyList<(string Id, string Name)>> PointsOfAsync(JourneyRig rig, string gameId, string profileId)
    {
        var history = await rig.Services.History.GetAsync(gameId, profileId, TestContext.Current.CancellationToken);
        Assert.True(history.Ok, history.Error?.Detail);
        return [.. history.Value.Points.OrderByDescending(point => point.CapturedAt).Select(point => (point.Id.Value, point.Name))];
    }
}
