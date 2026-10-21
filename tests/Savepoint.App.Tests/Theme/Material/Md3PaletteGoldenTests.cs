using System.Text;
using Savepoint.App.Theme.Material;

namespace Savepoint.App.Tests.Theme.Material;

public class Md3PaletteGoldenTests
{
    // One row per seed, mode and role for exactly what SavePoint v1 derives (contrast 0, tonal spot).
    public static TheoryData<string, bool, string, string> V1Roles()
    {
        var data = new TheoryData<string, bool, string, string>();
        foreach (var scheme in GoldenData.Current.Schemes.Where(s => s.Group == "v1"))
        {
            foreach (var (role, hex) in scheme.Colors)
            {
                data.Add(scheme.Seed, scheme.Dark, role, hex);
            }
        }

        return data;
    }

    public static TheoryData<string> V1Schemes() => SchemeIds(scheme => scheme.Group == "v1");

    public static TheoryData<string> ExtraSchemes() => SchemeIds(scheme => scheme.Group != "v1");

    [Theory]
    [MemberData(nameof(V1Roles))]
    public void Build_matches_the_reference_role(string seed, bool dark, string role, string expectedHex)
    {
        var seedArgb = GoldenData.ParseArgb("#FF" + GoldenData.Current.Seeds[seed][1..]);

        var roles = Md3Palette.Build(seedArgb, dark).ToRoleMap();

        Assert.Equal(expectedHex, GoldenData.Hex(roles[role]));
    }

    [Theory]
    [MemberData(nameof(V1Schemes))]
    public void Build_exposes_exactly_the_reference_role_set(string id)
    {
        var scheme = Find(id);

        var roles = Md3Palette.Build(SeedArgb(scheme), scheme.Dark).ToRoleMap();

        Assert.Equal(GoldenData.Current.Roles.Order(StringComparer.Ordinal), roles.Keys.Order(StringComparer.Ordinal));
        Assert.Equal(54, roles.Count);
    }

    [Theory]
    [MemberData(nameof(V1Schemes))]
    public void Tonal_spot_scheme_builds_the_reference_palettes(string id)
    {
        var expected = Find(id);

        var scheme = new SchemeTonalSpot(Hct.FromInt(SeedArgb(expected)), expected.Dark, 0.0);

        AssertPalette(expected, "primaryPalette", scheme.PrimaryPalette);
        AssertPalette(expected, "secondaryPalette", scheme.SecondaryPalette);
        AssertPalette(expected, "tertiaryPalette", scheme.TertiaryPalette);
        AssertPalette(expected, "neutralPalette", scheme.NeutralPalette);
        AssertPalette(expected, "neutralVariantPalette", scheme.NeutralVariantPalette);
    }

    [Theory]
    [MemberData(nameof(ExtraSchemes))]
    public void Other_variants_and_contrast_levels_match_the_reference(string id)
    {
        var expected = Find(id);

        var roles = Md3Palette.FromScheme(BuildScheme(expected)).ToRoleMap();

        var mismatches = new StringBuilder();
        foreach (var (role, hex) in expected.Colors)
        {
            if (GoldenData.Hex(roles[role]) != hex)
            {
                mismatches.AppendLine($"{role}: expected {hex}, actual {GoldenData.Hex(roles[role])}");
            }
        }

        Assert.True(mismatches.Length == 0, $"{id}:{Environment.NewLine}{mismatches}");
    }

    [Fact]
    public void The_golden_file_covers_every_group_the_tests_rely_on()
    {
        var groups = GoldenData.Current.Schemes.Select(s => s.Group).Distinct().Order(StringComparer.Ordinal);

        Assert.Equal(["content", "contrast", "fidelity", "monochrome", "v1"], groups);
        Assert.Equal(10, GoldenData.Current.Schemes.Count(s => s.Group == "v1"));
    }

    private static TheoryData<string> SchemeIds(Func<GoldenScheme, bool> filter)
    {
        var data = new TheoryData<string>();
        foreach (var scheme in GoldenData.Current.Schemes.Where(filter))
        {
            data.Add(scheme.Id);
        }

        return data;
    }

    private static GoldenScheme Find(string id) => GoldenData.Current.Schemes.Single(s => s.Id == id);

    private static uint SeedArgb(GoldenScheme scheme) => GoldenData.ParseArgb(scheme.SourceColor);

    private static void AssertPalette(GoldenScheme expected, string key, TonalPalette actual)
    {
        var palette = expected.Palettes[key];
        Numbers.AssertClose(palette.Hue, actual.Hue, $"{key} hue");
        Numbers.AssertClose(palette.Chroma, actual.Chroma, $"{key} chroma");
        Assert.Equal(palette.KeyColor, GoldenData.Hex(actual.KeyColor.ToInt()));
    }

    // Rebuilt from the recorded parameters, so non-tonal-spot schemes are assembled as the generator did.
    private static DynamicScheme BuildScheme(GoldenScheme scheme)
    {
        TonalPalette Palette(string key) =>
            TonalPalette.FromHueAndChroma(scheme.Palettes[key].Hue, scheme.Palettes[key].Chroma);

        var variant = scheme.Variant switch
        {
            "tonalSpot" => Variant.TonalSpot,
            "monochrome" => Variant.Monochrome,
            "fidelity" => Variant.Fidelity,
            "content" => Variant.Content,
            _ => throw new InvalidOperationException($"Unknown variant {scheme.Variant}."),
        };

        return new DynamicScheme(new DynamicSchemeArgs(
            SeedArgb(scheme),
            variant,
            scheme.Contrast,
            scheme.Dark,
            Palette("primaryPalette"),
            Palette("secondaryPalette"),
            Palette("tertiaryPalette"),
            Palette("neutralPalette"),
            Palette("neutralVariantPalette")));
    }
}
