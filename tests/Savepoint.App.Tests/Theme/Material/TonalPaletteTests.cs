using System.Globalization;
using Savepoint.App.Theme.Material;

namespace Savepoint.App.Tests.Theme.Material;

public class TonalPaletteTests
{
    public static TheoryData<double, double, double, string> ToneSamples()
    {
        var data = new TheoryData<double, double, double, string>();
        foreach (var palette in GoldenData.Current.TonalPalettes)
        {
            foreach (var (tone, argb) in palette.Tones)
            {
                data.Add(palette.Hue, palette.Chroma, double.Parse(tone, CultureInfo.InvariantCulture), argb);
            }
        }

        return data;
    }

    public static TheoryData<double, double> PaletteInputs()
    {
        var data = new TheoryData<double, double>();
        foreach (var palette in GoldenData.Current.TonalPalettes)
        {
            data.Add(palette.Hue, palette.Chroma);
        }

        return data;
    }

    public static TheoryData<string> Seeds()
    {
        var data = new TheoryData<string>();
        foreach (var sample in GoldenData.Current.TonalFromInt)
        {
            data.Add(sample.Seed);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(ToneSamples))]
    public void Tone_matches_the_reference(double hue, double chroma, double tone, string expected)
    {
        var palette = TonalPalette.FromHueAndChroma(hue, chroma);

        Assert.Equal(expected, GoldenData.Hex(palette.Tone(tone)));
    }

    [Theory]
    [MemberData(nameof(PaletteInputs))]
    public void KeyColor_matches_the_reference(double hue, double chroma)
    {
        var expected = GoldenData.Current.TonalPalettes.Single(p => p.Hue == hue && p.Chroma == chroma);

        var key = TonalPalette.FromHueAndChroma(hue, chroma).KeyColor;

        Assert.Equal(expected.KeyColor, GoldenData.Hex(key.ToInt()));
        Numbers.AssertClose(expected.KeyHue, key.Hue, "key hue");
        Numbers.AssertClose(expected.KeyChroma, key.Chroma, "key chroma");
        Numbers.AssertClose(expected.KeyTone, key.Tone, "key tone");
    }

    [Theory]
    [MemberData(nameof(PaletteInputs))]
    public void Palette_keeps_the_requested_hue_and_chroma(double hue, double chroma)
    {
        var palette = TonalPalette.FromHueAndChroma(hue, chroma);

        Assert.Equal(hue, palette.Hue);
        Assert.Equal(chroma, palette.Chroma);
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void FromInt_uses_the_seed_hue_chroma_and_itself_as_key_color(string seed)
    {
        var expected = GoldenData.Current.TonalFromInt.Single(p => p.Seed == seed);

        var palette = TonalPalette.FromInt(GoldenData.ParseArgb("#FF" + seed[1..]));

        Numbers.AssertClose(expected.Hue, palette.Hue, "hue");
        Numbers.AssertClose(expected.Chroma, palette.Chroma, "chroma");
        Assert.Equal(expected.KeyColor, GoldenData.Hex(palette.KeyColor.ToInt()));
        foreach (var (tone, argb) in expected.Tones)
        {
            Assert.Equal(argb, GoldenData.Hex(palette.Tone(double.Parse(tone, CultureInfo.InvariantCulture))));
        }
    }

    [Fact]
    public void Tone_is_the_same_on_repeated_calls()
    {
        var palette = TonalPalette.FromHueAndChroma(250.0, 36.0);

        Assert.Equal(palette.Tone(40.0), palette.Tone(40.0));
    }

    [Fact]
    public void Tone_gives_the_same_answers_when_one_palette_is_shared_across_threads()
    {
        double[] tones = [.. Enumerable.Range(0, 101).Select(tone => (double)tone)];
        var reference = TonalPalette.FromHueAndChroma(250.0, 36.0);
        var expected = tones.Select(reference.Tone).ToArray();
        var shared = TonalPalette.FromHueAndChroma(250.0, 36.0);

        var actual = new uint[tones.Length * 8];
        Parallel.For(0, actual.Length, i => actual[i] = shared.Tone(tones[i % tones.Length]));

        for (var i = 0; i < actual.Length; i++)
        {
            Assert.Equal(expected[i % tones.Length], actual[i]);
        }
    }
}
