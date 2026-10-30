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
    public async Task Settling_the_animations_before_a_render_leaves_the_nav_pills_finished_fading()
    {
        using var app = await ComposedApp.StartWithViewAsync();

        app.SettleAnimations();

        var layers = app.Find<ListBox>("NavList").GetVisualDescendants().OfType<ListBoxItem>().Select(SelectedLayer).ToList();
        Assert.True(layers.Count > 1, "the rail has more than one destination");
        Assert.Equal(1.0, layers[0]);
        Assert.All(layers.Skip(1), layer => Assert.Equal(0.0, layer));
    }
}
