using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Savepoint.App.Theme;
using Savepoint.Core.Catalog;

namespace Savepoint.App.Tests.Theme;

public class ThemeServiceLiveSwitchTests
{
    [AvaloniaFact]
    public void Accent_switch_updates_the_same_brush_instance_in_place()
    {
        var harness = new ThemeHarness();
        harness.Service.Apply(ThemePreference.Dark, AccentColorId.Blue);
        var brush = harness.BrushOf("Primary");

        harness.Service.SetAccent(AccentColorId.Crimson);

        Assert.Same(brush, harness.BrushOf("Primary"));
        Assert.Equal(ThemeTestSupport.ToColor(ThemeTestSupport.PaletteRoles(AccentColorId.Crimson, dark: true)["Primary"]), brush.Color);
        Assert.Equal(brush.Color, harness.ColorOf("Primary"));
    }

    [AvaloniaFact]
    public void Mode_switch_updates_every_brush_in_place()
    {
        var harness = new ThemeHarness();
        harness.Service.Apply(ThemePreference.Dark, AccentColorId.Violet);
        var before = ThemeTestSupport.PaletteRoles(AccentColorId.Violet, dark: true).Keys.ToDictionary(role => role, harness.BrushOf);

        harness.Service.SetPreference(ThemePreference.Light);

        var light = ThemeTestSupport.PaletteRoles(AccentColorId.Violet, dark: false);
        foreach (var (role, brush) in before)
        {
            Assert.Same(brush, harness.BrushOf(role));
            Assert.Equal(ThemeTestSupport.ToColor(light[role]), brush.Color);
        }
    }

    [AvaloniaFact]
    public void Changed_is_raised_once_per_apply()
    {
        var harness = new ThemeHarness();
        var raised = 0;
        harness.Service.Changed += (_, _) => raised++;

        harness.Service.Apply(ThemePreference.Dark, AccentColorId.Blue);
        harness.Service.SetAccent(AccentColorId.Green);

        Assert.Equal(2, raised);
    }

    // A control that resolves its color with a dynamic resource must show the new color without being rebuilt.
    [AvaloniaFact]
    public void A_dynamic_resource_bound_control_renders_the_new_accent_after_a_switch()
    {
        var harness = new ThemeHarness();
        harness.Service.Apply(ThemePreference.Dark, AccentColorId.Blue);
        var swatch = new Border { Width = 40, Height = 40, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top };
        var window = new Window { Width = 100, Height = 100, Content = swatch, Resources = harness.Resources, RequestedThemeVariant = ThemeVariant.Dark };
        swatch.Bind(Border.BackgroundProperty, swatch.GetResourceObservable(ThemeKeys.Brush("Primary")));

        var blue = ThemeTestSupport.PixelAt(ThemeTestSupport.Render(window), 20, 20);
        harness.Service.SetAccent(AccentColorId.Crimson);
        var crimson = ThemeTestSupport.PixelAt(window.CaptureRenderedFrame()!, 20, 20);

        Assert.Equal(harness.ColorOf("Primary"), crimson);
        Assert.NotEqual(blue, crimson);
        Assert.Equal(ThemeTestSupport.ToColor(ThemeTestSupport.PaletteRoles(AccentColorId.Blue, dark: true)["Primary"]), blue);
    }

    [AvaloniaFact]
    public void A_dynamic_resource_bound_control_renders_the_new_mode_after_a_switch()
    {
        var harness = new ThemeHarness();
        harness.Service.Apply(ThemePreference.Dark, AccentColorId.Blue);
        var panel = new Border { Width = 100, Height = 100 };
        var window = new Window { Width = 100, Height = 100, Content = panel, Resources = harness.Resources, RequestedThemeVariant = ThemeVariant.Dark };
        panel.Bind(Border.BackgroundProperty, panel.GetResourceObservable(ThemeKeys.Brush("Surface")));

        var dark = ThemeTestSupport.PixelAt(ThemeTestSupport.Render(window), 50, 50);
        harness.Service.SetPreference(ThemePreference.Light);
        var light = ThemeTestSupport.PixelAt(window.CaptureRenderedFrame()!, 50, 50);

        Assert.Equal(Color.Parse("#101418"), dark);
        Assert.Equal(harness.ColorOf("Surface"), light);
        Assert.True(light.R > dark.R);
    }
}
