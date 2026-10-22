using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Savepoint.App.Composition;
using Savepoint.App.Features.Common;
using Savepoint.App.Features.Common.Toasts;
using Savepoint.App.Features.Games;
using Savepoint.App.Features.Shell;
using Savepoint.App.Tests.Fakes;
using Savepoint.App.Tests.Features.Shell;

namespace Savepoint.App.Tests.Features.Games;

public class GamesCompositionTests
{
    [Fact]
    public async Task The_composition_builds_a_games_screen_from_the_apps_services()
    {
        var backend = FakeBackend.Mockup();
        var rig = await GamesRig.StartAsync(backend);
        var services = new AppServices(backend.Games, backend.Profiles, backend.Launch, backend.Settings, backend.History, backend.Jobs, backend.AppStatus, backend.Artwork);
        var context = new FeatureContext(services, rig.Dialogs, rig.Text, new ToastHostViewModel(TimeProvider.System, new ImmediateUiDispatcher()), new ImmediateUiDispatcher());

        var factory = FeatureComposition.GamesScreen(context, null);

        Assert.NotNull(factory);
        Assert.Equal(NavDestination.Games, factory.Destination);
        var screen = Assert.IsType<GamesScreenViewModel>(factory.Create(rig.Shell));
        await screen.ReloadAsync();
        Assert.Equal(3, screen.Profiles.Count);
        Assert.False(screen.CanEditGame);
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
