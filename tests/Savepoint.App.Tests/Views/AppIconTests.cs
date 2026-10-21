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
