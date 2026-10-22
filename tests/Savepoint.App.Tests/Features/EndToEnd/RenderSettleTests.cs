using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;

namespace Savepoint.App.Tests.Features.EndToEnd;

// A render is a picture of the finished screen, not of a fade that is half way: the nav pills fade in and out over a moment, and the
// app visits every screen on its way to the one being drawn.
public class RenderSettleTests
{
    private static double SelectedLayer(ListBoxItem item) =>
        item.GetVisualDescendants().OfType<Border>().First(border => border.Name == "PART_Selected").Opacity;

    [AvaloniaFact]
    public async Task A_render_is_taken_after_the_nav_pills_have_finished_fading()
    {
        using var app = await ComposedApp.StartWithViewAsync();

        app.Render("settle", "nav-pills");

        var pills = app.Find<ListBox>("NavList").GetVisualDescendants().OfType<ListBoxItem>().ToList();
        Assert.Equal([1.0, 0.0, 0.0], pills.Select(SelectedLayer));
    }
}
