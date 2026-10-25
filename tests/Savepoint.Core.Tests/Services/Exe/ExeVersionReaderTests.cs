using System.Text;
using Savepoint.Core.Services.Exe;

namespace Savepoint.Core.Tests.Services.Exe;

public class ExeVersionReaderTests
{
    [Fact]
    public void A_compiled_assembly_reports_its_product_version()
    {
        // The SDK writes InformationalVersion into the Win32 version resource as ProductVersion; the test project pins it.
        var bytes = File.ReadAllBytes(typeof(ExeVersionReaderTests).Assembly.Location);

        Assert.Equal("7.8.9-fixture", ExeVersionReader.FromPe(bytes));
    }

    [Fact]
    public void The_product_version_string_wins_over_the_file_version_string()
    {
        var bytes = PeFixture.WithVersion(new Version(1, 0, 0, 0), new Version(1, 0, 0, 0), PeStringTable.English(("FileVersion", "2.0.0.0"), ("ProductVersion", "3.1.4")));

        Assert.Equal("3.1.4", ExeVersionReader.FromPe(bytes));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("0.0.0.0")]
    [InlineData("0, 0, 0, 0")]
    public void The_file_version_string_is_used_when_the_product_version_string_says_nothing(string product)
    {
        var bytes = PeFixture.WithVersion(null, null, PeStringTable.English(("ProductVersion", product), ("FileVersion", "2.5.1.0")));

        Assert.Equal("2.5.1.0", ExeVersionReader.FromPe(bytes));
    }

    [Fact]
    public void The_file_version_string_is_used_when_there_is_no_product_version_string_at_all()
    {
        var bytes = PeFixture.WithVersion(null, null, PeStringTable.English(("FileVersion", "4.4.4")));

        Assert.Equal("4.4.4", ExeVersionReader.FromPe(bytes));
    }

    [Theory]
    [InlineData(1, 6, 640, 0, "1.6.640")]
    [InlineData(2, 0, 0, 0, "2.0")]
    [InlineData(1, 2, 3, 4, "1.2.3.4")]
    [InlineData(0, 9, 0, 0, "0.9")]
    [InlineData(0, 0, 0, 1, "0.0.0.1")]
    public void The_fixed_file_version_is_the_last_resort_with_trailing_zero_parts_dropped(int major, int minor, int build, int revision, string expected)
    {
        var bytes = PeFixture.WithVersion(new Version(major, minor, build, revision), null, PeStringTable.English());

        Assert.Equal(expected, ExeVersionReader.FromPe(bytes));
    }

    [Fact]
    public void A_zero_fixed_version_with_no_strings_is_no_version()
    {
        Assert.Null(ExeVersionReader.FromPe(PeFixture.WithVersion(new Version(0, 0, 0, 0), new Version(0, 0, 0, 0))));
        Assert.Null(ExeVersionReader.FromPe(PeFixture.WithVersion(null, null)));
    }

    [Fact]
    public void A_version_string_beats_a_fixed_version_even_when_it_has_padding_zeros()
    {
        var bytes = PeFixture.WithVersion(new Version(9, 9, 9, 9), null, PeStringTable.English(("FileVersion", "1.2.3.0")));

        Assert.Equal("1.2.3.0", ExeVersionReader.FromPe(bytes));
    }

    [Theory]
    [InlineData("  1.2.3  ", "1.2.3")]
    [InlineData("\t1.2.3\r\n", "1.2.3")]
    [InlineData("1, 0, 0, 1", "1.0.0.1")]
    [InlineData("1,0,0,1", "1.0.0.1")]
    [InlineData("1.6.640-beta", "1.6.640-beta")]
    [InlineData("v2 (build 7)", "v2 (build 7)")]
    public void A_version_string_is_trimmed_and_the_comma_form_becomes_dotted(string raw, string expected)
    {
        var bytes = PeFixture.WithVersion(null, null, PeStringTable.English(("ProductVersion", raw)));

        Assert.Equal(expected, ExeVersionReader.FromPe(bytes));
    }

    [Fact]
    public void A_later_language_table_supplies_the_version_when_the_first_has_none()
    {
        var bytes = PeFixture.WithVersion(
            null,
            null,
            PeStringTable.English(("ProductVersion", ""), ("CompanyName", "Somebody")),
            new PeStringTable(0x0407, 0x04B0, ("ProductVersion", "6.0.2")));

        Assert.Equal("6.0.2", ExeVersionReader.FromPe(bytes));
    }

    // Shapes below come from the version resources of real installed games.
    [Theory]
    [InlineData("++Game+release.1.4.7-CL-196638", "1.4.7")]
    [InlineData("++FactoryGame+rel-main-0.3.7-xmas-CL-140083", "0.3.7")]
    [InlineData("++oak2+release+oak2+1.11.0.0-buildmachine-nodevcontent-noaudio+Promoted-CL-4906557", "1.11.0.0")]
    public void An_unreal_build_id_that_carries_the_game_version_shows_just_that_version(string buildId, string expected)
    {
        var bytes = PeFixture.WithVersion(new Version(4, 27, 2, 0), null, PeStringTable.English(("ProductVersion", buildId)));

        Assert.Equal(expected, ExeVersionReader.FromPe(bytes));
    }

    [Theory]
    [InlineData("++Project+SN2-Release-CL-131439")]
    [InlineData("++UE4+Release-4.26-CL-0")]
    [InlineData("++UE427+ue427_release-CL-48954")]
    [InlineData("++FactoryGame+rel-main-anniversary-2026-CL-513885")]
    [InlineData("Subnautica2-CL-131439")]
    [InlineData("UE5-CL-0")]
    [InlineData("Dev-CL-0")]
    public void An_unreal_build_id_without_a_game_version_is_no_version_and_the_engine_version_is_not_a_fallback(string buildId)
    {
        var bytes = PeFixture.WithVersion(new Version(4, 27, 2, 0), null, PeStringTable.English(("ProductVersion", buildId), ("FileVersion", buildId)));

        Assert.Null(ExeVersionReader.FromPe(bytes));
    }

    [Fact]
    public void An_unreal_build_id_gives_way_to_a_real_file_version_string()
    {
        var bytes = PeFixture.WithVersion(null, null, PeStringTable.English(("ProductVersion", "++UE4+Release-4.26-CL-0"), ("FileVersion", "2.3.4")));

        Assert.Equal("2.3.4", ExeVersionReader.FromPe(bytes));
    }

    [Fact]
    public void A_unity_player_reports_no_version_because_its_numbers_belong_to_the_engine()
    {
        var modern = PeFixture.WithVersion(
            new Version(2022, 3, 62, 28864),
            null,
            PeStringTable.English(("FileVersion", "2022.3.62.7762112"), ("ProductVersion", "2022.3.62f2 (7670c08855a9)")));
        var older = PeFixture.WithVersion(
            new Version(2019, 4, 18, 4260),
            null,
            PeStringTable.English(("FileVersion", "2019.4.18.3346596"), ("ProductVersion", "2019.4.18.3346596"), ("Unity Version", "2019.4.18f1_3310a4d4f880")));
        var oldest = PeFixture.WithVersion(
            new Version(5, 6, 5, 34324),
            null,
            PeStringTable.English(("FileVersion", "5.6.5.1082900"), ("ProductVersion", "5.6.5.1082900"), ("Unity Version", "5.6.5p4_10861494ddb7")));

        Assert.Null(ExeVersionReader.FromPe(modern));
        Assert.Null(ExeVersionReader.FromPe(older));
        Assert.Null(ExeVersionReader.FromPe(oldest));
    }

    [Theory]
    [InlineData("1.0.0.0")]
    [InlineData("1.0")]
    [InlineData("1, 0, 0, 0")]
    public void The_placeholder_one_point_zero_is_no_version(string placeholder)
    {
        var bytes = PeFixture.WithVersion(new Version(1, 0, 0, 0), null, PeStringTable.English(("ProductVersion", placeholder), ("FileVersion", placeholder)));

        Assert.Null(ExeVersionReader.FromPe(bytes));
        Assert.Null(ExeVersionReader.FromPe(PeFixture.WithVersion(new Version(1, 0, 0, 0), null, PeStringTable.English())));
    }

    [Fact]
    public void A_placeholder_product_version_gives_way_to_a_real_file_version_string()
    {
        var bytes = PeFixture.WithVersion(null, null, PeStringTable.English(("ProductVersion", "1.0.0.0"), ("FileVersion", "1.6.2.0")));

        Assert.Equal("1.6.2.0", ExeVersionReader.FromPe(bytes));
    }

    [Theory]
    [InlineData("^VersionString^")]
    [InlineData("SanctuaryRPG")]
    public void A_version_string_with_no_digit_is_no_version(string text)
    {
        var bytes = PeFixture.WithVersion(new Version(1, 0, 0, 0), null, PeStringTable.English(("ProductVersion", text), ("FileVersion", text)));

        Assert.Null(ExeVersionReader.FromPe(bytes));
    }

    [Fact]
    public void The_fixed_version_is_not_used_when_a_string_was_present_but_rejected()
    {
        var bytes = PeFixture.WithVersion(new Version(4, 27, 2, 0), null, PeStringTable.English(("ProductVersion", "1.0.0.0")));

        Assert.Null(ExeVersionReader.FromPe(bytes));
    }

    [Fact]
    public void The_fixed_version_of_an_engine_vendors_executable_is_the_engine_version()
    {
        var bytes = PeFixture.WithVersion(new Version(4, 20, 3, 0), null, PeStringTable.English(("CompanyName", "Epic Games, Inc"), ("ProductName", "Ancestors")));

        Assert.Null(ExeVersionReader.FromPe(bytes));
    }

    [Fact]
    public void An_executable_named_as_the_engine_itself_has_no_game_version()
    {
        var bytes = PeFixture.WithVersion(
            new Version(4, 27, 2, 0),
            null,
            PeStringTable.English(("ProductName", "Unreal Engine"), ("ProductVersion", "4.27.2"), ("InternalName", "QUBE")));

        Assert.Null(ExeVersionReader.FromPe(bytes));
    }

    [Fact]
    public void A_pe_file_with_a_resource_directory_but_no_version_is_no_version()
    {
        Assert.Null(ExeVersionReader.FromPe(PeFixture.WithOnlyAManifest()));
    }

    [Fact]
    public void A_pe_file_with_no_resources_is_no_version()
    {
        Assert.Null(ExeVersionReader.FromPe(PeFixture.WithoutResources()));
    }

    [Fact]
    public void Input_that_is_not_a_pe_file_is_no_version_and_never_throws()
    {
        var noise = new byte[4096];
        new Random(12345).NextBytes(noise);
        var startsLikeAPe = new byte[4096];
        new Random(678).NextBytes(startsLikeAPe);
        startsLikeAPe[0] = (byte)'M';
        startsLikeAPe[1] = (byte)'Z';

        Assert.Null(ExeVersionReader.FromPe(null));
        Assert.Null(ExeVersionReader.FromPe([]));
        Assert.Null(ExeVersionReader.FromPe([0x4D]));
        Assert.Null(ExeVersionReader.FromPe(noise));
        Assert.Null(ExeVersionReader.FromPe(startsLikeAPe));
        Assert.Null(ExeVersionReader.FromPe(Encoding.UTF8.GetBytes("#!/bin/sh\nexec ./game\n")));
        Assert.Null(ExeVersionReader.FromPe(new byte[] { 0x7F, (byte)'E', (byte)'L', (byte)'F', 2, 1, 1, 0 }));
    }

    [Fact]
    public void A_truncated_pe_file_never_throws_and_never_reports_a_wrong_version()
    {
        var whole = PeFixture.WithVersion(new Version(1, 2, 3, 4), null, PeStringTable.English(("ProductVersion", "5.6.7")));
        var real = File.ReadAllBytes(typeof(ExeVersionReaderTests).Assembly.Location);

        foreach (var original in new[] { (whole, "5.6.7"), (real, "7.8.9-fixture") })
        {
            var cuts = Enumerable.Range(0, 64).Select(i => original.Item1.Length * i / 64).Concat([0, 1, 2, 63, 64, 65, 255, 256, 511, 512, 513]).Distinct();
            foreach (var length in cuts.Where(l => l < original.Item1.Length))
            {
                var result = ExeVersionReader.FromPe(original.Item1[..length]);

                Assert.True(result is null || result == original.Item2, $"a file cut to {length} bytes reported '{result}'");
            }
        }

        Assert.Null(ExeVersionReader.FromPe(whole[..(whole.Length - 8)]));
    }

    [Fact]
    public void A_property_list_reports_the_short_version_string()
    {
        var plist = Plist("""
            <key>CFBundleName</key><string>Cool Game</string>
            <key>CFBundleVersion</key><string>1204</string>
            <key>CFBundleShortVersionString</key><string>  2.4.1 </string>
            """);

        Assert.Equal("2.4.1", ExeVersionReader.FromInfoPlist(plist));
    }

    [Fact]
    public void A_property_list_without_the_key_or_with_an_empty_value_is_no_version()
    {
        Assert.Null(ExeVersionReader.FromInfoPlist(Plist("<key>CFBundleVersion</key><string>1204</string>")));
        Assert.Null(ExeVersionReader.FromInfoPlist(Plist("<key>CFBundleShortVersionString</key><string>  </string>")));
        Assert.Null(ExeVersionReader.FromInfoPlist(Plist("<key>CFBundleShortVersionString</key><true/>")));
        Assert.Null(ExeVersionReader.FromInfoPlist(Plist("<key>CFBundleShortVersionString</key>")));
    }

    [Fact]
    public void A_nested_key_with_the_same_name_is_not_the_bundle_version()
    {
        var plist = Plist("<key>Extra</key><dict><key>CFBundleShortVersionString</key><string>9.9</string></dict>");

        Assert.Null(ExeVersionReader.FromInfoPlist(plist));
    }

    [Fact]
    public void A_binary_property_list_a_malformed_one_and_noise_are_no_version()
    {
        Assert.Null(ExeVersionReader.FromInfoPlist(Encoding.ASCII.GetBytes("bplist00Ô\u0001\u0002")));
        Assert.Null(ExeVersionReader.FromInfoPlist(Encoding.UTF8.GetBytes("<plist><dict><key>CFBundleShortVersionString</key><string>1.0")));
        Assert.Null(ExeVersionReader.FromInfoPlist(null));
        Assert.Null(ExeVersionReader.FromInfoPlist([]));
        var noise = new byte[512];
        new Random(99).NextBytes(noise);
        Assert.Null(ExeVersionReader.FromInfoPlist(noise));
    }

    [Fact]
    public void A_property_list_never_pulls_in_an_external_entity()
    {
        using var temp = new TempFolder();
        var secret = temp.WriteText("secret.txt", "TOP-SECRET-CONTENT");
        var uri = new Uri(secret).AbsoluteUri;
        var plist = Encoding.UTF8.GetBytes(
            $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE plist [<!ENTITY leak SYSTEM "{uri}">]>
            <plist version="1.0"><dict><key>CFBundleShortVersionString</key><string>&leak;</string></dict></plist>
            """);

        var version = ExeVersionReader.FromInfoPlist(plist);

        Assert.DoesNotContain("TOP-SECRET", version ?? string.Empty);
    }

    private static byte[] Plist(string entries) =>
        Encoding.UTF8.GetBytes(
            $"""
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
            <plist version="1.0">
            <dict>
            {entries}
            </dict>
            </plist>
            """);
}
