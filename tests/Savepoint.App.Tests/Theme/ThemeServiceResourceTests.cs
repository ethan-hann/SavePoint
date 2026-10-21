using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Styling;
using Savepoint.App.Theme;
using Savepoint.Core.Catalog;

namespace Savepoint.App.Tests.Theme;

public class ThemeServiceResourceTests
{
    public static TheoryData<AccentColorId, bool> EveryAccentAndMode()
    {
        var data = new TheoryData<AccentColorId, bool>();
        foreach (var accent in Enum.GetValues<AccentColorId>())
        {
            data.Add(accent, false);
            data.Add(accent, true);
        }

        return data;
    }

    [AvaloniaTheory]
    [InlineData(AccentColorId.Blue, 0xFF2986CCu)]
    [InlineData(AccentColorId.Violet, 0xFF7C4DFFu)]
    [InlineData(AccentColorId.Green, 0xFF2E7D32u)]
    [InlineData(AccentColorId.Amber, 0xFFF9A825u)]
    [InlineData(AccentColorId.Crimson, 0xFFC2185Bu)]
    public void Accent_seeds_are_the_spec_presets(AccentColorId accent, uint seed)
    {
        Assert.Equal(seed, AccentSeeds.For(accent));
    }

    [AvaloniaTheory]
    [MemberData(nameof(EveryAccentAndMode))]
    public void Every_palette_role_is_published_as_color_and_brush_with_the_palettes_exact_value(AccentColorId accent, bool dark)
    {
        var harness = new ThemeHarness();

        harness.Service.Apply(dark ? ThemePreference.Dark : ThemePreference.Light, accent);

        var roles = ThemeTestSupport.PaletteRoles(accent, dark);
        Assert.NotEmpty(roles);
        foreach (var (role, argb) in roles)
        {
            Assert.Equal(ThemeTestSupport.ToColor(argb), harness.ColorOf(role));
            Assert.Equal(ThemeTestSupport.ToColor(argb), harness.BrushOf(role).Color);
        }
    }

    [AvaloniaFact]
    public void Blue_seed_dark_roles_are_the_known_library_values()
    {
        var harness = new ThemeHarness();

        harness.Service.Apply(ThemePreference.Dark, AccentColorId.Blue);

        Assert.Equal(Color.Parse("#9DCBFB"), harness.ColorOf("Primary"));
        Assert.Equal(Color.Parse("#101418"), harness.ColorOf("Surface"));
        Assert.Equal(Color.Parse("#272A2F"), harness.ColorOf("SurfaceContainerHigh"));
        Assert.Equal(Color.Parse("#124A73"), harness.ColorOf("PrimaryContainer"));
    }

    [AvaloniaFact]
    public void Key_scheme_is_Md3_role_then_Color_or_Brush()
    {
        var harness = new ThemeHarness();

        harness.Service.Apply(ThemePreference.Light, AccentColorId.Blue);

        Assert.IsType<Color>(harness.Value("Md3OnPrimaryContainerColor"));
        Assert.IsType<SolidColorBrush>(harness.Value("Md3OnPrimaryContainerBrush"));
        Assert.IsType<Color>(harness.Value("Md3PrimaryPaletteKeyColor"));
    }

    [AvaloniaTheory]
    [InlineData(false, "#775A00", "#FFFFFF", "#FFDF97", "#251A00")]
    [InlineData(true, "#EFC148", "#3E2E00", "#5A4400", "#FFDF97")]
    public void Warning_roles_use_the_gold_hue_at_the_tones_error_uses(bool dark, string main, string on, string container, string onContainer)
    {
        var harness = new ThemeHarness();

        harness.Service.Apply(dark ? ThemePreference.Dark : ThemePreference.Light, AccentColorId.Crimson);

        Assert.Equal(Color.Parse(main), harness.ColorOf("Warning"));
        Assert.Equal(Color.Parse(on), harness.ColorOf("OnWarning"));
        Assert.Equal(Color.Parse(container), harness.ColorOf("WarningContainer"));
        Assert.Equal(Color.Parse(onContainer), harness.ColorOf("OnWarningContainer"));
    }

    [AvaloniaTheory]
    [InlineData(false, "#276C2B", "#FFFFFF", "#ABF5A3", "#002204")]
    [InlineData(true, "#90D889", "#003909", "#075315", "#ABF5A3")]
    public void Success_roles_use_the_green_hue_at_the_tones_error_uses(bool dark, string main, string on, string container, string onContainer)
    {
        var harness = new ThemeHarness();

        harness.Service.Apply(dark ? ThemePreference.Dark : ThemePreference.Light, AccentColorId.Amber);

        Assert.Equal(Color.Parse(main), harness.ColorOf("Success"));
        Assert.Equal(Color.Parse(on), harness.ColorOf("OnSuccess"));
        Assert.Equal(Color.Parse(container), harness.ColorOf("SuccessContainer"));
        Assert.Equal(Color.Parse(onContainer), harness.ColorOf("OnSuccessContainer"));
    }

    [AvaloniaFact]
    public void Dark_error_callout_fill_is_the_mockups_warm_dark_red()
    {
        var harness = new ThemeHarness();

        harness.Service.Apply(ThemePreference.Dark, AccentColorId.Blue);

        Assert.Equal(Color.Parse("#3B2A28"), harness.ColorOf("ErrorCalloutBackground"));
    }

    [AvaloniaTheory]
    [InlineData("Error")]
    [InlineData("Warning")]
    [InlineData("Success")]
    public void Callout_borders_sit_apart_from_their_fill_in_both_modes(string family)
    {
        var harness = new ThemeHarness();

        foreach (var preference in new[] { ThemePreference.Light, ThemePreference.Dark })
        {
            harness.Service.Apply(preference, AccentColorId.Blue);

            Assert.NotEqual(harness.ColorOf($"{family}CalloutBackground"), harness.ColorOf($"{family}CalloutBorder"));
            Assert.Equal(0xFF, harness.ColorOf($"{family}CalloutBackground").A);
        }
    }

    [AvaloniaTheory]
    [InlineData("ScrimOverlay", "Scrim", 0.45)]
    [InlineData("StateHover", "OnSurface", 0.08)]
    [InlineData("StatePressed", "OnSurface", 0.12)]
    [InlineData("DisabledContainer", "OnSurface", 0.12)]
    [InlineData("DisabledContent", "OnSurface", 0.38)]
    [InlineData("ProgressTrack", "Primary", 0.16)]
    public void Translucent_roles_are_a_palette_color_at_a_fixed_opacity(string role, string source, double opacity)
    {
        var harness = new ThemeHarness();

        harness.Service.Apply(ThemePreference.Dark, AccentColorId.Violet);

        var derived = harness.ColorOf(role);
        var basis = harness.ColorOf(source);
        Assert.Equal(basis.R, derived.R);
        Assert.Equal(basis.G, derived.G);
        Assert.Equal(basis.B, derived.B);
        Assert.Equal((byte)Math.Round(opacity * 255), derived.A);
    }

    [AvaloniaTheory]
    [InlineData(true, 0.075)]
    [InlineData(false, 0.06)]
    public void Selected_row_is_the_surface_tinted_toward_primary(bool dark, double amount)
    {
        var harness = new ThemeHarness();

        harness.Service.Apply(dark ? ThemePreference.Dark : ThemePreference.Light, AccentColorId.Blue);

        var surface = harness.ColorOf("Surface");
        var primary = harness.ColorOf("Primary");
        var tint = harness.ColorOf("SelectedRow");
        Assert.Equal(0xFF, tint.A);
        Assert.Equal((byte)Math.Round(surface.R + ((primary.R - surface.R) * amount)), tint.R);
        Assert.Equal((byte)Math.Round(surface.G + ((primary.G - surface.G) * amount)), tint.G);
        Assert.Equal((byte)Math.Round(surface.B + ((primary.B - surface.B) * amount)), tint.B);
    }

    [AvaloniaTheory]
    [InlineData(true, "SurfaceContainer")]
    [InlineData(false, "SurfaceContainerLowest")]
    public void Card_surface_is_the_container_tone_in_dark_and_the_lightest_in_light(bool dark, string source)
    {
        var harness = new ThemeHarness();

        harness.Service.Apply(dark ? ThemePreference.Dark : ThemePreference.Light, AccentColorId.Blue);

        Assert.Equal(harness.ColorOf(source), harness.ColorOf("Card"));
    }

    [AvaloniaTheory]
    [InlineData("ToolTipBackground", "InverseSurface")]
    [InlineData("ToolTipForeground", "InverseOnSurface")]
    [InlineData("MenuFlyoutPresenterBackground", "SurfaceContainer")]
    [InlineData("MenuFlyoutItemForeground", "OnSurface")]
    [InlineData("ScrollBarThumbFillPointerOver", "ScrollThumbHover")]
    public void Fluent_keys_for_kept_controls_point_at_palette_brushes(string fluentKey, string role)
    {
        var harness = new ThemeHarness();

        harness.Service.Apply(ThemePreference.Dark, AccentColorId.Blue);

        Assert.Same(harness.BrushOf(role), harness.Value(fluentKey));
    }

    [AvaloniaFact]
    public void Every_bridged_fluent_key_follows_its_role_after_a_switch()
    {
        var harness = new ThemeHarness();
        harness.Service.Apply(ThemePreference.Dark, AccentColorId.Blue);
        Assert.NotEmpty(FluentBridge.Brushes);

        harness.Service.Apply(ThemePreference.Light, AccentColorId.Crimson);

        foreach (var (fluentKey, role) in FluentBridge.Brushes)
        {
            Assert.Same(harness.BrushOf(role), harness.Value(fluentKey));
        }

        foreach (var (fluentKey, role) in FluentBridge.Colors)
        {
            Assert.Equal(harness.ColorOf(role), harness.Value(fluentKey));
        }
    }

    [AvaloniaTheory]
    [InlineData(ThemePreference.Light, false, false)]
    [InlineData(ThemePreference.Dark, false, true)]
    [InlineData(ThemePreference.System, true, true)]
    [InlineData(ThemePreference.System, false, false)]
    public void Fluent_variant_is_always_set_explicitly_to_the_resolved_mode(ThemePreference preference, bool systemIsDark, bool expectDark)
    {
        var harness = new ThemeHarness(systemIsDark);

        harness.Service.Apply(preference, AccentColorId.Blue);

        var variant = Assert.Single(harness.Variants);
        Assert.Equal(expectDark ? ThemeVariant.Dark : ThemeVariant.Light, variant);
        Assert.Equal(expectDark, harness.Service.IsDark);
    }

    [AvaloniaFact]
    public void Service_remembers_the_applied_preference_and_accent()
    {
        var harness = new ThemeHarness();

        harness.Service.Apply(ThemePreference.Dark, AccentColorId.Green);

        Assert.Equal(ThemePreference.Dark, harness.Service.Preference);
        Assert.Equal(AccentColorId.Green, harness.Service.Accent);
    }
}
