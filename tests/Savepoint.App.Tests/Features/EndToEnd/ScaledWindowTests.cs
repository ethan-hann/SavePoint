using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Savepoint.App.Features.Common.Errors;
using Savepoint.App.Tests.Fakes;
using Savepoint.App.Tests.Theme;
using Savepoint.Core.Api;

namespace Savepoint.App.Tests.Features.EndToEnd;

// SavePoint has to work where the work area is 960x490 (200% on a 1080p screen) or 910x464 (150% on 1366x768). The audit of every
// screen and dialog at those sizes is in AppStateAuditTests; this is the one behavior that needs a short window of its own.
public class ScaledWindowTests
{
    [AvaloniaFact]
    public async Task A_card_that_appears_below_the_table_is_scrolled_into_view_in_a_short_window()
    {
        using var app = await ComposedApp.StartWithViewAsync(width: 910, height: 432);
        app.Backend.FailNext<ILaunchApi>(nameof(ILaunchApi.GetPreviewAsync), SampleScenario.FileInUseError);

        _ = app.Games.Profiles.Single(row => row.Id == SampleScenario.ModdedId).PlayCommand.ExecuteAsync(null);
        app.Settle();

        var card = app.All<ErrorCardView>().Single();
        var scroller = card.GetVisualAncestors().OfType<ScrollViewer>().FirstOrDefault();
        Assert.NotNull(scroller);
        var area = LayoutAudit.BoundsIn(app.Window, scroller);
        var shown = LayoutAudit.BoundsIn(app.Window, card);
        Assert.True(shown.Top >= area.Top - 1 && shown.Bottom <= area.Bottom + 1, $"The card at {shown} is not inside the scroll area at {area}.");
    }
}
