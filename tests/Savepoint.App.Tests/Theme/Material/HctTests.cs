using Savepoint.App.Theme.Material;

namespace Savepoint.App.Tests.Theme.Material;

public class HctTests
{
    [Fact]
    public void FromInt_reads_hue_chroma_and_tone_like_the_reference()
    {
        var mismatches = new GoldenMismatches();
        foreach (var sample in GoldenData.Current.HctFromArgb)
        {
            mismatches.Row();
            var row = $"FromInt({sample.Argb})";
            var hct = Hct.FromInt(GoldenData.ParseArgb(sample.Argb));

            mismatches.Exact(row, "argb", sample.Argb, GoldenData.Hex(hct.ToInt()));
            mismatches.Close(row, "hue", sample.Hue, hct.Hue);
            mismatches.Close(row, "chroma", sample.Chroma, hct.Chroma);
            mismatches.Close(row, "tone", sample.Tone, hct.Tone);
            mismatches.Exact(row, "argb after From(hue, chroma, tone)", sample.BackArgb, GoldenData.Hex(Hct.From(hct.Hue, hct.Chroma, hct.Tone).ToInt()));
        }

        mismatches.AssertNone("Hct.FromInt", 25);
    }

    [Fact]
    public void From_solves_to_the_same_color_as_the_reference()
    {
        var mismatches = new GoldenMismatches();
        foreach (var sample in GoldenData.Current.HctSolved)
        {
            mismatches.Row();
            var row = $"From(hue {sample.Hue:R}, chroma {sample.Chroma:R}, tone {sample.Tone:R})";
            var hct = Hct.From(sample.Hue, sample.Chroma, sample.Tone);

            mismatches.Exact(row, "argb", sample.Argb, GoldenData.Hex(hct.ToInt()));
            mismatches.Close(row, "hue", sample.OutHue, hct.Hue);
            mismatches.Close(row, "chroma", sample.OutChroma, hct.Chroma);
            mismatches.Close(row, "tone", sample.OutTone, hct.Tone);
        }

        mismatches.AssertNone("Hct.From", 280);
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
