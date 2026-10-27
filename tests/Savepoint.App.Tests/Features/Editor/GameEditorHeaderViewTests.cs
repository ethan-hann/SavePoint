using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;

namespace Savepoint.App.Tests.Features.Editor;

// The editor header has to fit the smallest window, whatever the interface size: a layout transform scales the whole window, so the
// width the header sees stays 640 px.
public class GameEditorHeaderViewTests
{
    private static readonly string[] _headerButtons = ["BackButton", "ExportButton", "ImportButton", "PickTemplateButton", "SaveButton"];

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Every_header_button_ends_inside_the_smallest_window(bool existingGame)
    {
        using var harness = await EditorViewHarness.StartAsync(width: 640, height: 420);
        if (existingGame)
        {
            harness.Edit();
        }
        else
        {
            harness.New();
        }

        harness.Settle();

        var shown = _headerButtons.Select(name => harness.Find<Button>(name)).Where(button => button.IsEffectivelyVisible).ToList();
        Assert.Contains(shown, button => button.Name == "SaveButton");
        foreach (var button in shown)
        {
            var right = button.TranslatePoint(new Point(button.Bounds.Width, 0), harness.Window)!.Value.X;
            Assert.True(right <= harness.Window.ClientSize.Width + 0.5, $"{button.Name} ends at {right}, past the window's {harness.Window.ClientSize.Width}.");
        }
    }

    [AvaloniaFact]
    public async Task The_header_buttons_stay_beside_the_title_when_the_window_is_wide()
    {
        using var harness = await EditorViewHarness.StartAsync();
        harness.Edit();
        harness.Settle();

        var title = harness.Find<TextBlock>("TitleText");
        var save = harness.Find<Button>("SaveButton");

        var titleTop = title.TranslatePoint(new Point(0, 0), harness.View)!.Value.Y;
        var saveTop = save.TranslatePoint(new Point(0, 0), harness.View)!.Value.Y;
        Assert.True(Math.Abs(titleTop - saveTop) < save.Bounds.Height, "Save changes sits on the title's row in a wide window.");
    }
}
