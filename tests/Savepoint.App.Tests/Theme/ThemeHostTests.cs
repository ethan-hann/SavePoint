using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Savepoint.Core.Catalog;

namespace Savepoint.App.Tests.Theme;

// The shared Application outlives every test, so test hosts must hand the theme back as they found it.
public class ThemeHostTests
{
    [AvaloniaFact]
    public void Disposing_a_host_restores_the_applications_default_theme()
    {
        var theme = ((App)Application.Current!).Theme;

        using (var host = new ThemeHost(new Border(), ThemePreference.Light, AccentColorId.Crimson))
        {
            theme.ReduceMotion = true;
            Assert.Equal(AccentColorId.Crimson, theme.Accent);
        }

        Assert.Equal(ThemePreference.System, theme.Preference);
        Assert.Equal(AccentColorId.Blue, theme.Accent);
        Assert.False(theme.ReduceMotion);
    }

    [AvaloniaFact]
    public void A_host_closes_its_window()
    {
        var host = new ThemeHost(new Border());
        var window = host.Window;
        Assert.True(window.IsVisible);

        host.Dispose();

        Assert.False(window.IsVisible);
    }
}
