using Savepoint.App.Theme.Material;

namespace Savepoint.App.Tests.Theme.Material;

public class ContrastTests
{
    public static TheoryData<double, double, double> RatioSamples()
    {
        var data = new TheoryData<double, double, double>();
        foreach (var sample in GoldenData.Current.Contrast.Ratios)
        {
            data.Add(sample.A, sample.B, sample.Ratio);
        }

        return data;
    }

    public static TheoryData<double, double, double> LighterSamples()
    {
        var data = new TheoryData<double, double, double>();
        foreach (var sample in GoldenData.Current.Contrast.Lighter)
        {
            data.Add(sample.Tone, sample.Ratio, sample.Result);
        }

        return data;
    }

    public static TheoryData<double, double, double> DarkerSamples()
    {
        var data = new TheoryData<double, double, double>();
        foreach (var sample in GoldenData.Current.Contrast.Darker)
        {
            data.Add(sample.Tone, sample.Ratio, sample.Result);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(RatioSamples))]
    public void RatioOfTones_matches_the_reference(double toneA, double toneB, double expected)
    {
        Numbers.AssertSame(expected, Contrast.RatioOfTones(toneA, toneB), "ratio");
    }

    [Theory]
    [MemberData(nameof(LighterSamples))]
    public void Lighter_matches_the_reference(double tone, double ratio, double expected)
    {
        Numbers.AssertSame(expected, Contrast.Lighter(tone, ratio), "lighter");
    }

    [Theory]
    [MemberData(nameof(DarkerSamples))]
    public void Darker_matches_the_reference(double tone, double ratio, double expected)
    {
        Numbers.AssertSame(expected, Contrast.Darker(tone, ratio), "darker");
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
