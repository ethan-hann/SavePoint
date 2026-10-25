using System.Buffers.Binary;
using Savepoint.App.Theme;
using Savepoint.App.Theme.Material;
using Savepoint.Core.Catalog;
using Savepoint.Testing;
using Savepoint.Tools.IconGen;
using SkiaSharp;

namespace Savepoint.App.Tests.Views;

public class AppIconTests
{
    private static readonly byte[] _pngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private static string IconFolder =>
        Path.Combine(TestEnvironment.RepoRoot ?? throw new InvalidOperationException("The tests did not run inside a checkout."), "src", "Savepoint.App", "Assets", "Icon");

    private static string IconFile(string name)
    {
        var path = Path.Combine(IconFolder, name);
        Assert.True(File.Exists(path), $"{name} is not in the icon folder.");
        return path;
    }

    [Fact]
    public void The_icon_colors_are_the_palettes_light_primary_and_on_primary_for_the_default_accent()
    {
        var palette = Md3Palette.Build(AccentSeeds.For(AccentColorId.Blue), dark: false);

        Assert.Equal(IconColors.Primary, palette.Primary);
        Assert.Equal(IconColors.OnPrimary, palette.OnPrimary);
    }

    [Fact]
    public void The_ico_holds_one_png_per_standard_size()
    {
        var bytes = File.ReadAllBytes(IconFile("savepoint.ico"));

        Assert.Equal(0, BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(0)));
        Assert.Equal(1, BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(2)));
        var count = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(4));
        var sizes = new List<int>();
        for (var i = 0; i < count; i++)
        {
            var entry = bytes.AsSpan(6 + (i * 16), 16);
            var size = entry[0] == 0 ? 256 : entry[0];
            sizes.Add(size);
            Assert.Equal(size, entry[1] == 0 ? 256 : entry[1]);
            Assert.Equal(32, BinaryPrimitives.ReadUInt16LittleEndian(entry[6..]));
            var length = (int)BinaryPrimitives.ReadUInt32LittleEndian(entry[8..]);
            var offset = (int)BinaryPrimitives.ReadUInt32LittleEndian(entry[12..]);
            var image = bytes.AsSpan(offset, length);
            Assert.True(image[..8].SequenceEqual(_pngSignature), $"The {size}px entry is not a PNG.");
            Assert.Equal(size, BinaryPrimitives.ReadInt32BigEndian(image[16..]));
            Assert.Equal(size, BinaryPrimitives.ReadInt32BigEndian(image[20..]));
        }

        Assert.Equal([16, 24, 32, 48, 64, 128, 256], sizes);
    }

    [Theory]
    [InlineData(16, true)]
    [InlineData(24, true)]
    [InlineData(32, true)]
    [InlineData(48, false)]
    [InlineData(64, false)]
    [InlineData(128, false)]
    [InlineData(256, false)]
    public void Frames_up_to_32_px_use_the_filled_glyph(int size, bool filled)
    {
        Assert.Equal(filled, IconFrames.UsesFilledGlyph(size));
    }

    // The share of a frame's pixels that are the glyph's color: a filled bookmark covers several times what the outline does.
    private static double GlyphShare(byte[] ico, int size)
    {
        var count = BinaryPrimitives.ReadUInt16LittleEndian(ico.AsSpan(4));
        for (var i = 0; i < count; i++)
        {
            var entry = ico.AsSpan(6 + (i * 16), 16);
            if ((entry[0] == 0 ? 256 : entry[0]) != size)
            {
                continue;
            }

            var length = (int)BinaryPrimitives.ReadUInt32LittleEndian(entry[8..]);
            var offset = (int)BinaryPrimitives.ReadUInt32LittleEndian(entry[12..]);
            using var bitmap = SKBitmap.Decode(ico.AsSpan(offset, length));
            var glyph = 0.0;
            for (var y = 0; y < bitmap.Height; y++)
            {
                for (var x = 0; x < bitmap.Width; x++)
                {
                    glyph += (uint)bitmap.GetPixel(x, y) == IconColors.OnPrimary ? 1 : 0;
                }
            }

            return glyph / (size * size);
        }

        throw new InvalidOperationException($"The ico has no {size}px frame.");
    }

    [Theory]
    [InlineData(16)]
    [InlineData(24)]
    [InlineData(32)]
    public void The_small_ico_frames_are_drawn_with_the_filled_glyph(int size)
    {
        var ico = File.ReadAllBytes(IconFile("savepoint.ico"));

        Assert.True(GlyphShare(ico, size) > 1.5 * GlyphShare(ico, 64), $"The {size}px frame looks like the outline glyph, not the filled one.");
    }

    private static byte[] Pixels(ReadOnlySpan<byte> png)
    {
        using var bitmap = SKBitmap.Decode(png);
        return bitmap.Bytes;
    }

    // The committed files must be what IconGen draws now, so editing the tool without regenerating (or the reverse) fails here.
    [Theory]
    [InlineData(16)]
    [InlineData(24)]
    [InlineData(32)]
    [InlineData(48)]
    [InlineData(64)]
    [InlineData(128)]
    [InlineData(256)]
    public void Each_committed_ico_frame_is_what_the_generator_draws(int size)
    {
        var fonts = Path.Combine(IconFolder, "..", "Fonts");
        using var outline = SKTypeface.FromFile(Path.Combine(fonts, "MaterialSymbolsRounded.ttf"));
        using var filled = SKTypeface.FromFile(Path.Combine(fonts, "MaterialSymbolsRoundedFilled.ttf"));
        var ico = File.ReadAllBytes(IconFile("savepoint.ico"));
        var count = BinaryPrimitives.ReadUInt16LittleEndian(ico.AsSpan(4));
        for (var i = 0; i < count; i++)
        {
            var entry = ico.AsSpan(6 + (i * 16), 16);
            if ((entry[0] == 0 ? 256 : entry[0]) != size)
            {
                continue;
            }

            var committed = ico.AsSpan((int)BinaryPrimitives.ReadUInt32LittleEndian(entry[12..]), (int)BinaryPrimitives.ReadUInt32LittleEndian(entry[8..]));
            Assert.True(Pixels(committed).SequenceEqual(Pixels(IconRenderer.Render(size, outline, filled))), $"The committed {size}px ico frame differs from what IconGen draws; run it again.");
            return;
        }

        Assert.Fail($"The ico has no {size}px frame.");
    }

    [Theory]
    [InlineData(256)]
    [InlineData(512)]
    [InlineData(1024)]
    public void Each_committed_png_is_what_the_generator_draws(int size)
    {
        var fonts = Path.Combine(IconFolder, "..", "Fonts");
        using var outline = SKTypeface.FromFile(Path.Combine(fonts, "MaterialSymbolsRounded.ttf"));
        using var filled = SKTypeface.FromFile(Path.Combine(fonts, "MaterialSymbolsRoundedFilled.ttf"));

        Assert.True(
            Pixels(File.ReadAllBytes(IconFile($"savepoint-{size}.png"))).SequenceEqual(Pixels(IconRenderer.Render(size, outline, filled))),
            $"The committed {size}px PNG differs from what IconGen draws; run it again.");
    }

    [Theory]
    [InlineData(256)]
    [InlineData(512)]
    [InlineData(1024)]
    public void Each_png_is_square_at_its_size(int size)
    {
        using var bitmap = SKBitmap.Decode(IconFile($"savepoint-{size}.png"));

        Assert.Equal(size, bitmap.Width);
        Assert.Equal(size, bitmap.Height);
    }

    [Fact]
    public void The_mark_is_a_primary_rounded_square_with_an_on_primary_glyph()
    {
        using var bitmap = SKBitmap.Decode(IconFile("savepoint-256.png"));
        var counts = new Dictionary<uint, int>();
        for (var y = 0; y < bitmap.Height; y++)
        {
            for (var x = 0; x < bitmap.Width; x++)
            {
                var pixel = (uint)bitmap.GetPixel(x, y);
                counts[pixel] = counts.GetValueOrDefault(pixel) + 1;
            }
        }

        // The corner lies outside a radius of 25% of the side; the middle of the top edge lies inside.
        Assert.Equal(0u, (uint)bitmap.GetPixel(0, 0) >> 24);
        Assert.Equal(IconColors.Primary, (uint)bitmap.GetPixel(128, 4));
        Assert.Equal(IconColors.Primary, counts.OrderByDescending(pair => pair.Value).First(pair => pair.Key >> 24 == 0xFF).Key);
        Assert.True(counts.GetValueOrDefault(IconColors.OnPrimary) > 400, "The glyph is missing or not in the on-primary color.");
    }
}
