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
