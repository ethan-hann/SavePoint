using Savepoint.App.Theme.Material;

namespace Savepoint.App.Tests.Theme.Material;

public class Md3PaletteTests
{
    [Fact]
    public void ToHex_drops_alpha_and_uses_uppercase_digits()
    {
        Assert.Equal("#9DCBFB", Md3Palette.ToHex(0xFF9DCBFB));
        Assert.Equal("#0A0B0C", Md3Palette.ToHex(0x800A0B0C));
        Assert.Equal("#000001", Md3Palette.ToHex(0x01000001));
    }

    [Fact]
    public void ToArgbHex_keeps_alpha_first()
    {
        Assert.Equal("#80123456", Md3Palette.ToArgbHex(0x80123456));
        Assert.Equal("#FF000000", Md3Palette.ToArgbHex(0xFF000000));
        Assert.Equal("#000000FF", Md3Palette.ToArgbHex(0x000000FF));
    }

    [Theory]
    [InlineData("#2986cc", 0xFF2986CCu)]
    [InlineData("#2986CC", 0xFF2986CCu)]
    [InlineData("2986cc", 0xFF2986CCu)]
    [InlineData("#80123456", 0x80123456u)]
    public void TryParseHex_reads_rgb_and_argb_forms(string text, uint expected)
    {
        Assert.True(Md3Palette.TryParseHex(text, out var argb));
        Assert.Equal(expected, argb);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("#")]
    [InlineData("#12")]
    [InlineData("#12345")]
    [InlineData("#1234567")]
    [InlineData("#GGGGGG")]
    [InlineData("#+12345")]
    [InlineData("# 12345")]
    public void TryParseHex_rejects_malformed_text(string? text)
    {
        Assert.False(Md3Palette.TryParseHex(text, out _));
    }

    [Fact]
    public void Build_records_the_seed_and_mode()
    {
        var palette = Md3Palette.Build(ReadmeTableTests.BlueSeed, dark: true);

        Assert.Equal(ReadmeTableTests.BlueSeed, palette.SeedArgb);
        Assert.True(palette.IsDark);
    }

    [Fact]
    public void Build_is_deterministic_and_value_equal()
    {
        Assert.Equal(Md3Palette.Build(ReadmeTableTests.BlueSeed, true), Md3Palette.Build(ReadmeTableTests.BlueSeed, true));
        Assert.NotEqual(Md3Palette.Build(ReadmeTableTests.BlueSeed, true), Md3Palette.Build(ReadmeTableTests.BlueSeed, false));
    }

    [Fact]
    public void Build_gives_every_role_an_opaque_color()
    {
        var roles = Md3Palette.Build(0xFF7C4DFF, dark: false).ToRoleMap();

        Assert.All(roles, role => Assert.Equal(0xFFu, role.Value >> 24));
    }

    [Fact]
    public void Build_is_safe_to_call_from_many_threads()
    {
        uint[] seeds = [0xFF2986CC, 0xFF7C4DFF, 0xFF2E7D32, 0xFFF9A825, 0xFFC2185B];
        var expected = seeds.ToDictionary(seed => seed, seed => Md3Palette.Build(seed, true));

        var results = new Md3Palette[seeds.Length * 8];
        Parallel.For(0, results.Length, i => results[i] = Md3Palette.Build(seeds[i % seeds.Length], true));

        for (var i = 0; i < results.Length; i++)
        {
            Assert.Equal(expected[seeds[i % seeds.Length]], results[i]);
        }
    }
}
