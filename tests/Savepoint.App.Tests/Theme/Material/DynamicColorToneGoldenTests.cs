using Savepoint.App.Theme.Material;

namespace Savepoint.App.Tests.Theme.Material;

// Hand-built colors, so the tone logic is checked on inputs the Material role table never produces
// (tones inside the awkward zone, two backgrounds, every polarity, clamped deltas).
public class DynamicColorToneGoldenTests
{
    public static TheoryData<int> PairCases() => Indices("pair");

    public static TheoryData<int> SingleCases() => Indices("single");

    [Theory]
    [MemberData(nameof(PairCases))]
    public void Tone_delta_pairs_resolve_to_the_reference_tones(int index)
    {
        var spec = GoldenData.Current.ToneCases[index];
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

        Numbers.AssertClose(spec.ExpectedA, colorA.GetTone(scheme), $"case {index} color a");
        Numbers.AssertClose(spec.ExpectedB!.Value, colorB.GetTone(scheme), $"case {index} color b");
    }

    [Theory]
    [MemberData(nameof(SingleCases))]
    public void Colors_with_one_or_two_backgrounds_resolve_to_the_reference_tone(int index)
    {
        var spec = GoldenData.Current.ToneCases[index];
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

        Numbers.AssertClose(spec.ExpectedA, foreground.GetTone(scheme), $"case {index}");
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

    private static TheoryData<int> Indices(string kind)
    {
        var data = new TheoryData<int>();
        var cases = GoldenData.Current.ToneCases;
        for (var i = 0; i < cases.Length; i++)
        {
            if (cases[i].Kind == kind)
            {
                data.Add(i);
            }
        }

        return data;
    }

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
