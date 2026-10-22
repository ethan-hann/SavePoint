using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Savepoint.App.Features.Common.Errors;
using Savepoint.App.Tests.Theme;

namespace Savepoint.App.Tests.Features.EndToEnd;

// When the library cannot be read the page says so and offers Try again; it is never a blank Games screen with buttons that do nothing.
public class LibraryErrorViewTests
{
    [AvaloniaFact]
    public async Task An_unreadable_library_shows_the_error_card_with_Try_again_and_no_dead_game_buttons()
    {
        using var app = await AppStates.OpenAsync("library-error");

        var card = app.All<ErrorCardView>().Single(view => view.IsEffectivelyVisible);
        var buttons = card.GetVisualDescendants().OfType<Button>().Where(button => button.IsEffectivelyVisible).Select(button => button.Content).ToList();
        Assert.Contains("Try again", buttons);
        Assert.DoesNotContain(app.All<Button>(), button => button.IsEffectivelyVisible && AutomationAudit.NameOf(button) is "Edit game" or "New profile");
        Assert.Empty(AutomationAudit.Problems(app.Window));
        app.Render("shell", "library-error");
    }
}
