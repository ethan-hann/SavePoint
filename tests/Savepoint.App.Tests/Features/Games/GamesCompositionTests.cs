using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Savepoint.App.Composition;
using Savepoint.App.Features.Common;
using Savepoint.App.Features.Common.Toasts;
using Savepoint.App.Features.Games;
using Savepoint.App.Features.Shell;
using Savepoint.App.Tests.Fakes;
using Savepoint.App.Tests.Features.Shell;
using Savepoint.Core.Api;

namespace Savepoint.App.Tests.Features.Games;

public class GamesCompositionTests
{
    [Fact]
    public async Task The_composition_builds_a_games_screen_from_the_apps_services()
    {
        var backend = FakeBackend.Sample();
        var rig = await GamesRig.StartAsync(backend);
        var services = new AppServices(backend.Games, backend.Profiles, backend.Launch, backend.Settings, backend.History, backend.Jobs, backend.AppStatus, backend.Artwork);
        var context = new FeatureContext(services, rig.Dialogs, rig.Text, new ToastHostViewModel(TimeProvider.System, new ImmediateUiDispatcher()), new ImmediateUiDispatcher());

        var factory = FeatureComposition.GamesScreen(context, new RecordingEditorFactory());

        Assert.NotNull(factory);
        Assert.Equal(NavDestination.Games, factory.Destination);
        var screen = Assert.IsType<GamesScreenViewModel>(factory.Create(rig.Shell));
        await screen.ReloadAsync();
        Assert.Equal(3, screen.Profiles.Count);
    }

    [Fact]
    public async Task The_composition_gives_the_games_screen_the_apps_folder_watcher_and_window()
    {
        var backend = FakeBackend.Sample();
        var rig = await GamesRig.StartAsync(backend);
        var watcher = new FakeFolderWatcher();
        var window = new FakeWindowActivity();
        var clock = new ManualTimeProvider();
        var services = new AppServices(backend.Games, backend.Profiles, backend.Launch, backend.Settings, backend.History, backend.Jobs, backend.AppStatus, backend.Artwork)
        {
            FolderWatcher = watcher,
        };
        var context = new FeatureContext(services, rig.Dialogs, rig.Text, new ToastHostViewModel(TimeProvider.System, new ImmediateUiDispatcher()), new ImmediateUiDispatcher(), clock, Window: window);
        var screen = Assert.IsType<GamesScreenViewModel>(FeatureComposition.GamesScreen(context, new RecordingEditorFactory()).Create(rig.Shell));
        screen.OnShown();
        await screen.ReloadAsync();
        var watch = Assert.Single(watcher.Running);
        var reads = backend.Calls.To<IProfilesApi>(nameof(IProfilesApi.GetRowStatsAsync)).Count;

        window.Become(active: false);
        watch.RaiseChanged();
        clock.Advance(GamesScreenViewModel.ChangeSettleTime);
        await YieldTurnsAsync();
        Assert.Equal(reads, backend.Calls.To<IProfilesApi>(nameof(IProfilesApi.GetRowStatsAsync)).Count);

        window.Become(active: true);
        clock.Advance(GamesScreenViewModel.ChangeSettleTime);
        await YieldTurnsAsync();
        Assert.Equal(reads + 1, backend.Calls.To<IProfilesApi>(nameof(IProfilesApi.GetRowStatsAsync)).Count);
    }

    // The fake backend answers without waiting, so a few turns of the scheduler let a read run to its end.
    private static async Task YieldTurnsAsync()
    {
        for (var turn = 0; turn < 10; turn++)
        {
            await Task.Yield();
        }
    }

    [AvaloniaFact]
    public async Task The_app_shows_the_games_screen_for_the_games_destination()
    {
        using var harness = await ShellViewHarness.StartAsync();

        var screen = Assert.Single(harness.All<GamesScreenView>());

        Assert.True(screen.IsEffectivelyVisible);
        Assert.Contains("Modded playthrough - 7-2-26", harness.All<TextBlock>().Where(block => block.IsEffectivelyVisible).Select(block => block.Text));
    }
}
