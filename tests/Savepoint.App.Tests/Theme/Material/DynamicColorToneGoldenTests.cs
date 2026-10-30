using Savepoint.App.Theme.Material;

namespace Savepoint.App.Tests.Theme.Material;

// Hand-built colors reach tone branches the Material role table never does.
public class DynamicColorToneGoldenTests
{
    [Fact]
    public void Tone_delta_pairs_resolve_to_the_reference_tones()
    {
        var mismatches = new GoldenMismatches();
        foreach (var (index, spec) in Cases("pair"))
        {
            mismatches.Row();
            var (a, b) = ResolvePair(spec);
            var row = $"case {index} ({Describe(spec)})";
            mismatches.Close(row, "color a", spec.ExpectedA, a);
            mismatches.Close(row, "color b", spec.ExpectedB!.Value, b);
        }

        mismatches.AssertNone("Tone delta pairs", 400);
    }

    [Fact]
    public void Colors_with_one_or_two_backgrounds_resolve_to_the_reference_tone()
    {
        var mismatches = new GoldenMismatches();
        foreach (var (index, spec) in Cases("single"))
        {
            mismatches.Row();
            mismatches.Close($"case {index} ({Describe(spec)})", "tone", spec.ExpectedA, ResolveSingle(spec));
        }

        mismatches.AssertNone("Single colors", 450);
    }

    private static (double A, double B) ResolvePair(GoldenToneCase spec)
    {
        var palette = TonalPalette.FromHueAndChroma(250.0, 36.0);
        var scheme = SchemeFor(spec, palette);
        var background = DynamicColor.FromPalette("bg", _ => palette, _ => spec.BgTone, isBackground: true);
        ToneDeltaPair? pair = null;
        var colorA = DynamicColor.FromPalette(
            "a",
            _ => palette,
            _ => spec.ToneA,
            isBackground: true,
            background: _ => background,
            contrastCurve: Curve(spec.CurveA),
            toneDeltaPair: _ => pair!);
        var colorB = DynamicColor.FromPalette(
            "b",
            _ => palette,
            _ => spec.ToneB!.Value,
            isBackground: true,
            background: _ => background,
            contrastCurve: Curve(spec.CurveB!),
            toneDeltaPair: _ => pair!);
        pair = new ToneDeltaPair(colorA, colorB, spec.Delta!.Value, Polarity(spec.Polarity!), spec.StayTogether!.Value);

        return (colorA.GetTone(scheme), colorB.GetTone(scheme));
    }

    private static double ResolveSingle(GoldenToneCase spec)
    {
        var palette = TonalPalette.FromHueAndChroma(250.0, 36.0);
        var scheme = SchemeFor(spec, palette);
        var background = DynamicColor.FromPalette("bg", _ => palette, _ => spec.BgTone, isBackground: true);
        var second = DynamicColor.FromPalette("bg2", _ => palette, _ => spec.Bg2Tone ?? 0.0, isBackground: true);
        var foreground = DynamicColor.FromPalette(
            "fg",
            _ => palette,
            _ => spec.ToneA,
            isBackground: spec.IsBackground,
            background: _ => background,
            secondBackground: spec.Bg2Tone is null ? null : _ => second,
            contrastCurve: Curve(spec.CurveA));

        return foreground.GetTone(scheme);
    }

    [Fact]
    public void The_sweep_reaches_the_branches_it_exists_for()
    {
        var cases = GoldenData.Current.ToneCases;

        Assert.Contains(cases, c => c.Kind == "pair" && c.StayTogether == true && c.Dark);
        Assert.Contains(cases, c => c.Kind == "single" && c.IsBackground && c.Bg2Tone is null);
        Assert.Contains(cases, c => c.Kind == "single" && c.Bg2Tone is not null);
        Assert.Contains(cases, c => c.Kind == "pair" && c.Polarity == "darker");
        Assert.Contains(cases, c => c.Contrast < 0);
        Assert.Contains(cases, c => c.Contrast > 0);
    }

    private static IEnumerable<(int Index, GoldenToneCase Spec)> Cases(string kind) =>
        GoldenData.Current.ToneCases.Select((spec, index) => (index, spec)).Where(c => c.spec.Kind == kind);

    private static string Describe(GoldenToneCase spec) =>
        $"{(spec.Dark ? "dark" : "light")}, contrast {spec.Contrast:R}, background tone {spec.BgTone:R}, tone {spec.ToneA:R}";

    private static DynamicScheme SchemeFor(GoldenToneCase spec, TonalPalette palette) =>
        new(new DynamicSchemeArgs(0xFF2986CC, Variant.TonalSpot, spec.Contrast, spec.Dark, palette, palette, palette, palette, palette));

    private static ContrastCurve Curve(double[] anchors) => new(anchors[0], anchors[1], anchors[2], anchors[3]);

    private static TonePolarity Polarity(string name) => name switch
    {
        "darker" => TonePolarity.Darker,
        "lighter" => TonePolarity.Lighter,
        "nearer" => TonePolarity.Nearer,
        "farther" => TonePolarity.Farther,
        _ => throw new InvalidOperationException($"Unknown polarity {name}."),
    };
}
