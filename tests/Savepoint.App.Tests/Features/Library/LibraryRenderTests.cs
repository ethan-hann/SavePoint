using Avalonia.Headless.XUnit;
using Savepoint.App.Tests.Features.EndToEnd;

namespace Savepoint.App.Tests.Features.Library;

// The three dialogs of "Add games from your libraries" as they are drawn, in render-output/library/.
public class LibraryRenderTests
{
    private static void AssertRendered(string path) =>
        Assert.True(new FileInfo(path).Length > 10_000, "The render looks empty.");

    [AvaloniaTheory]
    [InlineData("library-picker")]
    [InlineData("library-picker-many")]
    [InlineData("library-confirm")]
    [InlineData("library-add-done")]
    public async Task Each_dialog_renders_to_a_png(string state)
    {
        using var app = await AppStates.OpenAsync(state);

        AssertRendered(app.Render("library", state));
    }

    [AvaloniaFact]
    public async Task The_first_run_screen_with_the_new_button_renders_to_a_png()
    {
        using var app = await AppStates.OpenAsync("first-run");

        AssertRendered(app.Render("library", "first-run"));
    }
}
