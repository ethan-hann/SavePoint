using System.Globalization;
using Savepoint.App.Theme.Material;

namespace Savepoint.App.Tests.Theme.Material;

public class TonalPaletteTests
{
    [Fact]
    public void Tone_matches_the_reference()
    {
        var mismatches = new GoldenMismatches();
        foreach (var golden in GoldenData.Current.TonalPalettes)
        {
            var palette = TonalPalette.FromHueAndChroma(golden.Hue, golden.Chroma);
            foreach (var (tone, argb) in golden.Tones)
            {
                mismatches.Row();
                mismatches.Exact($"palette(hue {golden.Hue:R}, chroma {golden.Chroma:R}).Tone({tone})", "argb", argb, GoldenData.Hex(palette.Tone(double.Parse(tone, CultureInfo.InvariantCulture))));
            }
        }

        mismatches.AssertNone("TonalPalette.Tone", 232);
    }

    [Fact]
    public void KeyColor_matches_the_reference()
    {
        var mismatches = new GoldenMismatches();
        foreach (var golden in GoldenData.Current.TonalPalettes)
        {
            mismatches.Row();
            var row = $"palette(hue {golden.Hue:R}, chroma {golden.Chroma:R})";
            var key = TonalPalette.FromHueAndChroma(golden.Hue, golden.Chroma).KeyColor;

            mismatches.Exact(row, "key color", golden.KeyColor, GoldenData.Hex(key.ToInt()));
            mismatches.Close(row, "key hue", golden.KeyHue, key.Hue);
            mismatches.Close(row, "key chroma", golden.KeyChroma, key.Chroma);
            mismatches.Close(row, "key tone", golden.KeyTone, key.Tone);
        }

        mismatches.AssertNone("TonalPalette.KeyColor", 8);
    }

    [Fact]
    public void Palette_keeps_the_requested_hue_and_chroma()
    {
        var mismatches = new GoldenMismatches();
        foreach (var golden in GoldenData.Current.TonalPalettes)
        {
            mismatches.Row();
            var row = $"palette(hue {golden.Hue:R}, chroma {golden.Chroma:R})";
            var palette = TonalPalette.FromHueAndChroma(golden.Hue, golden.Chroma);

            mismatches.Same(row, "hue", golden.Hue, palette.Hue);
            mismatches.Same(row, "chroma", golden.Chroma, palette.Chroma);
        }

        mismatches.AssertNone("TonalPalette hue and chroma", 8);
    }

    [Fact]
    public void FromInt_uses_the_seed_hue_chroma_and_itself_as_key_color()
    {
        var mismatches = new GoldenMismatches();
        foreach (var golden in GoldenData.Current.TonalFromInt)
        {
            mismatches.Row();
            var row = $"FromInt({golden.Seed})";
            var palette = TonalPalette.FromInt(GoldenData.ParseArgb("#FF" + golden.Seed[1..]));

            mismatches.Close(row, "hue", golden.Hue, palette.Hue);
            mismatches.Close(row, "chroma", golden.Chroma, palette.Chroma);
            mismatches.Exact(row, "key color", golden.KeyColor, GoldenData.Hex(palette.KeyColor.ToInt()));
            foreach (var (tone, argb) in golden.Tones)
            {
                mismatches.Exact(row, $"tone {tone}", argb, GoldenData.Hex(palette.Tone(double.Parse(tone, CultureInfo.InvariantCulture))));
            }
        }

        mismatches.AssertNone("TonalPalette.FromInt", 5);
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
