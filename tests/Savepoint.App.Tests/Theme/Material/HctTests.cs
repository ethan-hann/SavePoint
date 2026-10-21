using Savepoint.App.Theme.Material;

namespace Savepoint.App.Tests.Theme.Material;

public class HctTests
{
    public static TheoryData<string, double, double, double> ArgbSamples()
    {
        var data = new TheoryData<string, double, double, double>();
        foreach (var sample in GoldenData.Current.HctFromArgb)
        {
            data.Add(sample.Argb, sample.Hue, sample.Chroma, sample.Tone);
        }

        return data;
    }

    public static TheoryData<double, double, double, string, double, double, double> SolvedSamples()
    {
        var data = new TheoryData<double, double, double, string, double, double, double>();
        foreach (var sample in GoldenData.Current.HctSolved)
        {
            data.Add(sample.Hue, sample.Chroma, sample.Tone, sample.Argb, sample.OutHue, sample.OutChroma, sample.OutTone);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(ArgbSamples))]
    public void FromInt_reads_hue_chroma_and_tone_like_the_reference(string argb, double hue, double chroma, double tone)
    {
        var hct = Hct.FromInt(GoldenData.ParseArgb(argb));

        Assert.Equal(argb, GoldenData.Hex(hct.ToInt()));
        Numbers.AssertClose(hue, hct.Hue, "hue");
        Numbers.AssertClose(chroma, hct.Chroma, "chroma");
        Numbers.AssertClose(tone, hct.Tone, "tone");
    }

    [Theory]
    [MemberData(nameof(SolvedSamples))]
    public void From_solves_to_the_same_color_as_the_reference(
        double hue, double chroma, double tone, string argb, double outHue, double outChroma, double outTone)
    {
        var hct = Hct.From(hue, chroma, tone);

        Assert.Equal(argb, GoldenData.Hex(hct.ToInt()));
        Numbers.AssertClose(outHue, hct.Hue, "hue");
        Numbers.AssertClose(outChroma, hct.Chroma, "chroma");
        Numbers.AssertClose(outTone, hct.Tone, "tone");
    }

    [Fact]
    public void From_round_trips_through_FromInt()
    {
        var solved = Hct.From(250.0, 36.0, 40.0);

        var again = Hct.FromInt(solved.ToInt());

        Assert.Equal(solved.ToInt(), again.ToInt());
        Numbers.AssertSame(solved.Hue, again.Hue, "hue");
        Numbers.AssertSame(solved.Chroma, again.Chroma, "chroma");
    }
}
