using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Savepoint.App.Theme;
using Savepoint.App.Theme.Material;
using Savepoint.Core.Catalog;

namespace Savepoint.App.Tests.Theme;

// Findings from the theme review: the warning family is gold, the accent swatches need the raw seeds, and a recessed well shows in light mode.
public class ThemePolishTests
{
    // The gold the design draws the game editor's Steam callout in (#F5D488 text on an olive fill).
    private const uint _referenceGold = 0xFFF5D488;

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
    [InlineData(false, "Warning")]
    [InlineData(false, "WarningContainer")]
    [InlineData(false, "WarningCalloutBackground")]
    [InlineData(false, "WarningCalloutBorder")]
    [InlineData(true, "Warning")]
    [InlineData(true, "WarningContainer")]
    [InlineData(true, "WarningCalloutBackground")]
    [InlineData(true, "WarningCalloutBorder")]
    public void Warning_family_has_the_reference_gold_hue(bool dark, string role)
    {
        var harness = new ThemeHarness();

        harness.Service.Apply(dark ? ThemePreference.Dark : ThemePreference.Light, AccentColorId.Blue);

        var gold = Hct.FromInt(_referenceGold).Hue;
        var hue = Hct.FromInt(harness.ColorOf(role).ToUInt32()).Hue;
        Assert.InRange(hue, gold - 8, gold + 8);
    }

    [AvaloniaTheory]
    [MemberData(nameof(EveryAccentAndMode))]
    public void Seed_brushes_are_the_raw_accent_colors_whatever_accent_and_mode_are_applied(AccentColorId accent, bool dark)
    {
        var harness = new ThemeHarness();

        // Applied with a different accent, so a swatch cannot pass by following the current one.
        harness.Service.Apply(dark ? ThemePreference.Dark : ThemePreference.Light, accent == AccentColorId.Crimson ? AccentColorId.Blue : AccentColorId.Crimson);

        var expected = Color.FromUInt32(AccentSeeds.For(accent));
        Assert.Equal(expected, Assert.IsType<SolidColorBrush>(harness.Value($"Md3Seed{accent}Brush")).Color);
        Assert.Equal(expected, Assert.IsType<Color>(harness.Value($"Md3Seed{accent}Color")));
    }

    [AvaloniaTheory]
    [InlineData(ThemePreference.Light)]
    [InlineData(ThemePreference.Dark)]
    public void A_recessed_well_stands_out_from_the_card_it_sits_in(ThemePreference mode)
    {
        var well = new Border { Width = 90, Height = 36, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
        well.Classes.Add("recessed");
        var card = new Border { Padding = new Thickness(40), Child = well };
        card.Classes.Add("card");
        using var host = new ThemeHost(card, mode, width: 220, height: 130);

        var frame = ThemeTestSupport.Render(host.Window);
        var inCard = ThemeTestSupport.PixelAt(frame, 20, 20);
        var inWell = ThemeTestSupport.PixelAt(frame, 110, 65);

        var difference = Math.Max(Math.Abs(inCard.R - inWell.R), Math.Max(Math.Abs(inCard.G - inWell.G), Math.Abs(inCard.B - inWell.B)));
        Assert.True(difference >= 10, $"The well {inWell} is too close to the card {inCard} (largest channel difference {difference}).");
    }
}
