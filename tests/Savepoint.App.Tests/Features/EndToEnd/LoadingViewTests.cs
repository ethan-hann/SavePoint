using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Savepoint.App.Tests.Theme;

namespace Savepoint.App.Tests.Features.EndToEnd;

// Nothing on screen is ever an unexplained blank: a screen that is waiting says so.
public class LoadingViewTests
{
    [AvaloniaFact]
    public async Task Clicking_a_game_shows_a_named_loading_panel_in_place_of_a_blank_page_until_its_profiles_arrive()
    {
        using var app = await AppStates.OpenAsync("games-loading");

        var panel = app.Find<Border>("LoadingState");
        Assert.True(panel.IsEffectivelyVisible);
        Assert.Equal("Loading your profiles…", AutomationAudit.NameOf(panel));
        Assert.False(app.Find<Border>("Table").IsEffectivelyVisible);
        Assert.False(app.Find<Border>("EmptyState").IsEffectivelyVisible);
        Assert.Contains(app.All<ProgressBar>(), bar => bar.IsEffectivelyVisible && bar.IsIndeterminate && AutomationAudit.NameOf(bar) == "Loading your profiles…");
        app.Render("games", "loading-profiles");
    }
}
