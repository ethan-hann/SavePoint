using Avalonia.Headless.XUnit;
using Avalonia.Platform;
using Avalonia.Styling;
using Savepoint.App.Theme;
using Savepoint.Core.Catalog;

namespace Savepoint.App.Tests.Theme;

public class ThemeServiceSystemModeTests
{
    [AvaloniaFact]
    public void System_preference_follows_a_later_change_of_the_system()
    {
        var harness = new ThemeHarness(systemIsDark: false);
        harness.Service.Apply(ThemePreference.System, AccentColorId.Blue);
        var raised = 0;
        harness.Service.Changed += (_, _) => raised++;

        harness.System.Set(true);

        Assert.True(harness.Service.IsDark);
        Assert.Equal(ThemeVariant.Dark, harness.Variants[^1]);
        Assert.Equal(Avalonia.Media.Color.Parse("#101418"), harness.ColorOf("Surface"));
        Assert.Equal(1, raised);
    }

    [AvaloniaFact]
    public void System_preference_follows_the_system_back_to_light()
    {
        var harness = new ThemeHarness(systemIsDark: true);
        harness.Service.Apply(ThemePreference.System, AccentColorId.Blue);

        harness.System.Set(false);

        Assert.False(harness.Service.IsDark);
        Assert.Equal(ThemeVariant.Light, harness.Variants[^1]);
        Assert.Equal(Avalonia.Media.Color.Parse("#F7F9FF"), harness.ColorOf("Surface"));
    }

    [AvaloniaTheory]
    [InlineData(ThemePreference.Light, false)]
    [InlineData(ThemePreference.Dark, true)]
    public void Explicit_preference_ignores_system_changes(ThemePreference preference, bool expectDark)
    {
        var harness = new ThemeHarness(systemIsDark: !expectDark);
        harness.Service.Apply(preference, AccentColorId.Blue);
        var raised = 0;
        harness.Service.Changed += (_, _) => raised++;

        harness.System.Set(expectDark);
        harness.System.Set(!expectDark);

        Assert.Equal(expectDark, harness.Service.IsDark);
        Assert.Equal(0, raised);
    }

    [AvaloniaFact]
    public void Platform_source_reads_the_platforms_variant_and_raises_when_it_changes()
    {
        var platform = new FakePlatformSettings();
        var source = new PlatformSystemThemeSource(platform.Settings);
        var raised = 0;
        source.Changed += (_, _) => raised++;
        Assert.False(source.IsDark);

        platform.SetThemeVariant(PlatformThemeVariant.Dark);

        Assert.True(source.IsDark);
        Assert.Equal(1, raised);
    }

    [AvaloniaFact]
    public void Platform_source_without_platform_settings_reports_light()
    {
        var source = new PlatformSystemThemeSource(null);

        Assert.False(source.IsDark);
    }

    [AvaloniaFact]
    public void Service_on_the_platform_source_follows_a_simulated_operating_system_change()
    {
        var platform = new FakePlatformSettings();
        var variants = new List<ThemeVariant>();
        var resources = new Avalonia.Controls.ResourceDictionary();
        var service = new ThemeService(resources, new PlatformSystemThemeSource(platform.Settings), variants.Add);
        service.Apply(ThemePreference.System, AccentColorId.Blue);
        Assert.False(service.IsDark);

        platform.SetThemeVariant(PlatformThemeVariant.Dark);

        Assert.True(service.IsDark);
        Assert.Equal(ThemeVariant.Dark, variants[^1]);
        Assert.True(resources.TryGetResource("Md3SurfaceColor", null, out var surface));
        Assert.Equal(Avalonia.Media.Color.Parse("#101418"), Assert.IsType<Avalonia.Media.Color>(surface));
    }
}
