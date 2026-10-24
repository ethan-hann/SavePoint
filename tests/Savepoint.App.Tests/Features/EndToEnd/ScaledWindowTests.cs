using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Savepoint.App.Features.Common.Errors;
using Savepoint.App.Tests.Fakes;
using Savepoint.App.Tests.Theme;
using Savepoint.Core.Api;

namespace Savepoint.App.Tests.Features.EndToEnd;

// SavePoint has to work where the work area is 960x490 (200% on a 1080p screen) or 910x464 (150% on 1366x768): every screen and
// dialog in the states a person meets them shows its controls or scrolls to them, and nothing is squeezed to nothing.
public class ScaledWindowTests
{
    public static TheoryData<string, string> StatesAtScaledSizes()
    {
        var data = new TheoryData<string, string>();
        foreach (var state in AppStates.All.Select(row => row.Data))
        {
            foreach (var (name, _, _) in LayoutAudit.ScaledScreens)
            {
                data.Add(state, name);
            }
        }

        return data;
    }

    [AvaloniaFact]
    public async Task A_card_that_appears_below_the_table_is_scrolled_into_view_in_a_short_window()
    {
        using var app = await ComposedApp.StartWithViewAsync(width: 910, height: 432);
        app.Backend.FailNext<ILaunchApi>(nameof(ILaunchApi.GetPreviewAsync), MockupScenario.FileInUseError);

        _ = app.Games.Profiles.Single(row => row.Id == MockupScenario.ModdedId).PlayCommand.ExecuteAsync(null);
        app.Settle();

        var card = app.All<ErrorCardView>().Single();
        var scroller = card.GetVisualAncestors().OfType<ScrollViewer>().FirstOrDefault();
        Assert.NotNull(scroller);
        var area = LayoutAudit.BoundsIn(app.Window, scroller);
        var shown = LayoutAudit.BoundsIn(app.Window, card);
        Assert.True(shown.Top >= area.Top - 1 && shown.Bottom <= area.Bottom + 1, $"The card at {shown} is not inside the scroll area at {area}.");
    }

    [AvaloniaTheory]
    [MemberData(nameof(StatesAtScaledSizes))]
    public async Task Every_control_is_in_view_or_behind_a_scroll_area_at_a_scaled_size(string state, string screen)
    {
        var (_, width, height) = LayoutAudit.ScaledScreens.Single(size => size.Name == screen);
        using var app = await AppStates.OpenAsync(state);

        app.Window.Width = width;
        app.Window.Height = height;
        app.Settle();

        var problems = LayoutAudit.Problems(app.Window);
        Assert.True(problems.Count == 0, $"{state} at {screen} ({width}x{height}):{Environment.NewLine}{string.Join(Environment.NewLine, problems)}");
    }
}
