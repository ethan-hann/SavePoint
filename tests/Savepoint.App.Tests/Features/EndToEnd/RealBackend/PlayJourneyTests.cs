using Avalonia.Headless.XUnit;
using Savepoint.App.Features.History;
using Savepoint.App.Features.Launch;
using Savepoint.App.Features.Shell;
using Savepoint.Core.Api;

namespace Savepoint.App.Tests.Features.EndToEnd.RealBackend;

// Playing a profile: the stand-in game is started as a real program, writes saves while it runs, and exits; the app then offers to keep the
// session, and what it keeps shows up in History.
public class PlayJourneyTests
{
    [AvaloniaFact(Timeout = 180_000)]
    public async Task Play_starts_the_game_under_the_banner_and_keeping_the_session_adds_a_point_to_History()
    {
        await using var rig = await JourneyRig.StartAsync();
        var game = rig.NewGame("Play Game");
        await rig.AddGameAsync(game, ["--saves", game.Saves, "--save-every", "150", "--exit-after", "3000"]);
        var vanilla = await rig.CreateProfileAsync("Vanilla");
        await rig.WaitAsync(() => vanilla.IsActive, "the profile to show as active");
        var installBefore = FolderSnapshot.Of(game.Install);
        Assert.Empty(FolderSnapshot.Of(game.Saves));
        using var trail = new InlineTrail(rig.Games);

        // Everything is already in place, so there is no dialog: the banner says so, and the game starts as a real program.
        var play = vanilla.PlayCommand.ExecuteAsync(null);
        Assert.False(vanilla.CanPlay, "Play is still on offer while the check runs");
        await rig.FinishesAsync(play, "Play to hand the game over");
        await rig.WaitAsync(() => rig.Games.InlineContent is RunningCardViewModel, "the running card");
        Assert.True(trail.Showed(nameof(LaunchBannerViewModel), nameof(RunningCardViewModel)), string.Join(" > ", trail.Seen));
        var running = Assert.IsType<RunningCardViewModel>(rig.Games.InlineContent);
        Assert.Equal("Play Game is running", running.Title);
        Assert.StartsWith("Vanilla · started", running.Subtitle, StringComparison.Ordinal);
        Assert.Empty(rig.Main.Dialogs.Stack.OfType<LaunchDialogViewModel>());
        await rig.WaitAsync(() => vanilla.IsRunning, "the row to say the profile is running");
        Assert.Equal("Running", vanilla.PlayLabel);
        Assert.False(vanilla.CanPlay);
        rig.Render("2a-the-game-is-running");

        // The game writes its saves into the saves folder while it runs.
        await rig.WaitAsync(() => Directory.EnumerateFiles(game.Saves, "save-*.sav").Any(), "the game to write a save");

        // When it exits, the app asks what to do with the session. Keeping it as a new point is what it recommends.
        var postPlay = await rig.DialogAsync<PostPlayDialogViewModel>();
        Assert.StartsWith("You played for ", postPlay.Title, StringComparison.Ordinal);
        Assert.Equal([CompletionChoice.CaptureNew, CompletionChoice.UpdateExisting, CompletionChoice.Skip], postPlay.Choices.Select(choice => choice.Kind));
        Assert.Equal(CompletionChoice.CaptureNew, postPlay.SelectedChoice.Kind);
        Assert.Equal("Keep it", postPlay.PrimaryLabel);
        Assert.Contains("Replace \"Vanilla\" with this session", postPlay.Choices.Select(choice => choice.Title));
        var written = FolderSnapshot.Of(game.Saves);
        Assert.NotEmpty(written);
        Assert.Equal(rig.Text.Message("launch.postPlay.body", ("count", written.Count)), postPlay.Body);
        Assert.Empty(rig.Main.Dialogs.Stack.OfType<SwitchProgressDialogViewModel>());
        Assert.Null(rig.Games.InlineContent);
        rig.Render("2b-after-play-dialog");

        rig.Main.Toasts.DismissAll();
        postPlay.SelectedChoice.Name = "First session";
        await rig.FinishesAsync(postPlay.SaveCommand.ExecuteAsync(null), "the after-play dialog to answer");
        await rig.WaitAsync(() => vanilla.CanPlay && rig.Games.InlineContent is null && rig.ToastTexts.Contains("Took a snapshot of Play Game."), "the session to be saved");
        Assert.True(trail.Showed(nameof(LaunchBannerViewModel), nameof(RunningCardViewModel), nameof(LaunchSavingViewModel), "nothing"), string.Join(" > ", trail.Seen));
        await rig.WaitAsync(() => vanilla.IsActive && !vanilla.IsRunning, "the row to read active again");
        Assert.StartsWith("Loaded now · 2 snapshots", vanilla.SecondaryText, StringComparison.Ordinal);

        // History lists the new point, newest first, as a save with the same game version and mods.
        rig.Shell.Navigate(NavDestination.History);
        await rig.WaitAsync(() => rig.History.State == HistoryState.Loaded && rig.History.Saves.Count == 2, "History to list both points");
        Assert.Equal("Play Game · 2 snapshots", rig.History.Subtitle);
        Assert.Equal(["First session", "Vanilla"], rig.History.Saves.Select(save => save.Name));
        var newest = rig.History.Groups.SelectMany(group => group.Cards).First();
        Assert.Equal("First session", newest.Name);
        Assert.StartsWith("Saves only", newest.Contains, StringComparison.Ordinal);
        Assert.True(newest.IsLoaded, "the point the saves folder holds is marked as loaded");
        Assert.Equal(HistoryPointKind.SaveOnly, newest.Point.Kind);
        rig.Render("2c-history-with-the-new-point");

        // On disk, keeping the session left both folders as the game left them.
        Assert.Equal(installBefore, FolderSnapshot.Of(game.Install));
        Assert.Equal(written, FolderSnapshot.Of(game.Saves));
    }

    // A profile made from the one that is loaded, with a mod added first, shares that profile's saves until it saves its own.
    [AvaloniaFact(Timeout = 180_000)]
    public async Task Playing_a_profile_made_from_another_offers_to_replace_its_own_snapshot_and_its_row_counts_the_kept_session()
    {
        await using var rig = await JourneyRig.StartAsync();
        var game = rig.NewGame("Play Game");
        JourneyRig.WriteFile(Path.Combine(game.Saves, "slot1.sav"), "save one");
        await rig.AddGameAsync(game, ["--saves", game.Saves, "--save-every", "150", "--exit-after", "3000"]);
        var vanilla = await rig.CreateProfileAsync("Vanilla");
        JourneyRig.WriteFile(Path.Combine(game.Install, "mods", "big.bin"), "a very large mod");
        await Task.Delay(TimeSpan.FromMilliseconds(1100), TestContext.Current.CancellationToken);
        var heavy = await rig.CreateProfileAsync("Heavy mods");
        await rig.WaitAsync(() => heavy.IsActive && !vanilla.IsActive, "the rows to show the folders are on Heavy mods");

        await rig.FinishesAsync(heavy.PlayCommand.ExecuteAsync(null), "Play to hand the game over");
        var postPlay = await rig.DialogAsync<PostPlayDialogViewModel>();

        Assert.Contains("Replace \"Heavy mods\" with this session", postPlay.Choices.Select(choice => choice.Title));
        Assert.DoesNotContain("Replace \"Vanilla\" with this session", postPlay.Choices.Select(choice => choice.Title));
        rig.Main.Toasts.DismissAll();
        await rig.FinishesAsync(postPlay.SaveCommand.ExecuteAsync(null), "the after-play dialog to answer");
        await rig.WaitAsync(() => heavy.CanPlay && rig.Games.InlineContent is null && heavy.IsActive && !heavy.IsRunning, "the session to be saved");
        Assert.StartsWith("Loaded now · 2 snapshots", heavy.SecondaryText, StringComparison.Ordinal);
        Assert.StartsWith("1 snapshot", vanilla.SecondaryText, StringComparison.Ordinal);
    }
}
