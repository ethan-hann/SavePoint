using Savepoint.App.Theme.Material;

namespace Savepoint.App.Tests.Theme.Material;

public class ContrastTests
{
    [Fact]
    public void RatioOfTones_matches_the_reference()
    {
        var mismatches = new GoldenMismatches();
        foreach (var sample in GoldenData.Current.Contrast.Ratios)
        {
            mismatches.Row();
            mismatches.Same($"RatioOfTones({sample.A:R}, {sample.B:R})", "ratio", sample.Ratio, Contrast.RatioOfTones(sample.A, sample.B));
        }

        mismatches.AssertNone("Contrast.RatioOfTones", 64);
    }

    [Fact]
    public void Lighter_matches_the_reference()
    {
        var mismatches = new GoldenMismatches();
        foreach (var sample in GoldenData.Current.Contrast.Lighter)
        {
            mismatches.Row();
            mismatches.Same($"Lighter(tone {sample.Tone:R}, ratio {sample.Ratio:R})", "tone", sample.Result, Contrast.Lighter(sample.Tone, sample.Ratio));
        }

        mismatches.AssertNone("Contrast.Lighter", 48);
    }

    [Fact]
    public void Darker_matches_the_reference()
    {
        var mismatches = new GoldenMismatches();
        foreach (var sample in GoldenData.Current.Contrast.Darker)
        {
            mismatches.Row();
            mismatches.Same($"Darker(tone {sample.Tone:R}, ratio {sample.Ratio:R})", "tone", sample.Result, Contrast.Darker(sample.Tone, sample.Ratio));
        }

        mismatches.AssertNone("Contrast.Darker", 48);
    }

    [Fact]
    public void Unreachable_ratios_return_minus_one_from_the_safe_methods_and_the_edge_tone_from_the_unsafe_ones()
    {
        Assert.Equal(-1.0, Contrast.Lighter(100.0, 21.0));
        Assert.Equal(100.0, Contrast.LighterUnsafe(100.0, 21.0));
        Assert.Equal(-1.0, Contrast.Darker(0.0, 21.0));
        Assert.Equal(0.0, Contrast.DarkerUnsafe(0.0, 21.0));
    }

    [Fact]
    public void Out_of_range_tones_return_minus_one()
    {
        Assert.Equal(-1.0, Contrast.Lighter(-1.0, 3.0));
        Assert.Equal(-1.0, Contrast.Darker(101.0, 3.0));
    }
}
