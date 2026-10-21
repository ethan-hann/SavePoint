using Avalonia;
using Avalonia.Automation;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Savepoint.App.Features.Common.Art;
using Savepoint.App.Tests.Theme;
using Savepoint.App.Theme.Material;
using Savepoint.Core.Catalog;

namespace Savepoint.App.Tests.Features.Common;

public class ArtTileTests
{
    [Theory]
    [InlineData("", 0)]
    [InlineData("a", 97)]
    [InlineData("Zz", 32)]
    [InlineData("Skyrim Special Edition", 108)]
    [InlineData("Satisfactory", 182)]
    [InlineData("The Last Caretaker", 33)]
    public void The_hue_follows_the_original_apps_name_hash(string name, int expected)
    {
        Assert.Equal(expected, ArtTint.Hue(name));
    }

    [Fact]
    public void A_name_long_enough_to_overflow_the_hash_still_gives_a_hue_in_range()
    {
        var hue = ArtTint.Hue(new string('z', 400));

        Assert.InRange(hue, 0, 359);
    }

    [Fact]
    public void The_hue_counts_utf16_code_units()
    {
        var eAcute = ((char)0xE9).ToString();
        var gamepad = string.Concat((char)0xD83C, (char)0xDFAE);

        Assert.Equal(233, ArtTint.Hue(eAcute));
        Assert.Equal(298, ArtTint.Hue(gamepad));
    }

    [Fact]
    public void The_same_name_always_gives_the_same_tint()
    {
        Assert.Equal(ArtTint.Gradient("Skyrim", dark: true), ArtTint.Gradient("Skyrim", dark: true));
    }

    [Fact]
    public void Different_names_with_different_hues_give_different_tints()
    {
        Assert.NotEqual(ArtTint.Gradient("Skyrim Special Edition", dark: true), ArtTint.Gradient("Satisfactory", dark: true));
    }

    [Theory]
    [InlineData(true, 34, 24)]
    [InlineData(false, 84, 74)]
    public void The_gradient_runs_from_a_lighter_top_to_a_darker_bottom_at_tones_that_suit_the_mode(bool dark, double topTone, double bottomTone)
    {
        var (top, bottom) = ArtTint.Gradient("Satisfactory", dark);

        Assert.Equal(topTone, Hct.FromInt(top).Tone, 1.5);
        Assert.Equal(bottomTone, Hct.FromInt(bottom).Tone, 1.5);
        Assert.Equal(0xFF000000u, top & 0xFF000000u);
    }

    [Fact]
    public void The_tint_keeps_the_hue_of_the_name()
    {
        var (top, _) = ArtTint.Gradient("The Last Caretaker", dark: false);

        Assert.Equal(ArtTint.Hue("The Last Caretaker"), Hct.FromInt(top).Hue, 12.0);
    }

    [AvaloniaFact]
    public void A_tile_is_a_vertical_gradient_in_the_tint_of_its_game()
    {
        using var host = new ThemeHost(new ArtTile { GameName = "Satisfactory" }, ThemePreference.Dark);
        var tile = (ArtTile)host.Window.Content!;

        var gradient = Assert.IsType<LinearGradientBrush>(tile.Background);

        var (top, bottom) = ArtTint.Gradient("Satisfactory", dark: true);
        Assert.Equal(Color.FromUInt32(top), gradient.GradientStops[0].Color);
        Assert.Equal(Color.FromUInt32(bottom), gradient.GradientStops[1].Color);
        Assert.Equal(new RelativePoint(0.5, 0, RelativeUnit.Relative), gradient.StartPoint);
        Assert.Equal(new RelativePoint(0.5, 1, RelativeUnit.Relative), gradient.EndPoint);
    }

    [AvaloniaFact]
    public void Changing_the_game_name_re_tints_the_tile()
    {
        var tile = new ArtTile { GameName = "Skyrim Special Edition" };
        using var host = new ThemeHost(tile, ThemePreference.Dark);
        var before = ((LinearGradientBrush)tile.Background!).GradientStops[0].Color;

        tile.GameName = "Satisfactory";
        Dispatcher.UIThread.RunJobs();

        Assert.NotEqual(before, ((LinearGradientBrush)tile.Background!).GradientStops[0].Color);
    }

    [AvaloniaFact]
    public void Switching_between_light_and_dark_re_tints_the_tile_without_replacing_it()
    {
        var tile = new ArtTile { GameName = "Satisfactory" };
        using var host = new ThemeHost(tile, ThemePreference.Dark);
        var dark = ((LinearGradientBrush)tile.Background!).GradientStops[0].Color;

        ((App)Application.Current!).Theme.SetPreference(ThemePreference.Light);
        Dispatcher.UIThread.RunJobs();

        var light = ((LinearGradientBrush)tile.Background!).GradientStops[0].Color;
        Assert.NotEqual(dark, light);
        Assert.Equal(Color.FromUInt32(ArtTint.Gradient("Satisfactory", dark: false).Top), light);
    }

    [AvaloniaTheory]
    [InlineData(24, 5)]
    [InlineData(40, 8)]
    [InlineData(34, 8)]
    public void A_sized_tile_is_square_and_rounded_to_suit_its_size(double size, double radius)
    {
        var tile = new ArtTile { GameName = "x", Size = size };

        Assert.Equal(size, tile.Width);
        Assert.Equal(size, tile.Height);
        Assert.Equal(new CornerRadius(radius), tile.CornerRadius);
    }

    [AvaloniaFact]
    public void A_tile_has_the_shared_art_tile_class_and_is_decorative()
    {
        var tile = new ArtTile();

        Assert.Contains("art-tile", tile.Classes);
        Assert.Equal(AccessibilityView.Raw, AutomationProperties.GetAccessibilityView(tile));
    }
}
