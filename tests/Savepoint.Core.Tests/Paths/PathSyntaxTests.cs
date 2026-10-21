using Savepoint.Core.Paths;
using Savepoint.Core.Platform;

namespace Savepoint.Core.Tests.Paths;

public class PathSyntaxTests
{
    public static TheoryData<OsKind, string, bool> AbsoluteCases => new()
    {
        { OsKind.Windows, @"C:\Games", true },
        { OsKind.Windows, "c:/Games", true },
        { OsKind.Windows, @"D:\", true },
        { OsKind.Windows, @"\\server\share", true },
        { OsKind.Windows, @"\\server\share\Games", true },
        { OsKind.Windows, @"\\?\C:\Games", true },
        { OsKind.Windows, @"\\.\C:\Games", true },
        { OsKind.Windows, @"\Games", false },
        { OsKind.Windows, "C:Games", false },
        { OsKind.Windows, @"Games\Skyrim", false },
        { OsKind.Windows, @"\\server", false },
        { OsKind.Windows, "/etc", false },
        { OsKind.Windows, "", false },
        { OsKind.Linux, "/etc", true },
        { OsKind.Linux, "/", true },
        { OsKind.Linux, "etc/passwd", false },
        { OsKind.Linux, @"C:\Games", false },
        { OsKind.Linux, @"\etc", false },
        { OsKind.Linux, "", false },
        { OsKind.MacOS, "/Applications", true },
        { OsKind.MacOS, "Applications", false },
        { OsKind.MacOS, @"C:\Games", false },
    };

    public static TheoryData<OsKind, string, string> NormalizeCases => new()
    {
        { OsKind.Windows, "c:/a//b/./c/../d/", @"C:\a\b\d" },
        { OsKind.Windows, @"C:\..\..\x", @"C:\x" },
        { OsKind.Windows, @"C:\", @"C:\" },
        { OsKind.Windows, @"d:\Games\", @"D:\Games" },
        { OsKind.Windows, @"\\?\C:\Games\Sky", @"C:\Games\Sky" },
        { OsKind.Windows, @"\\server\share\a\..\b", @"\\server\share\b" },
        { OsKind.Windows, @"\\server\share", @"\\server\share\" },
        { OsKind.Windows, @"C:\a. \b ", @"C:\a. \b" },
        { OsKind.Windows, @"C:\Windows\ \..\System32", @"C:\Windows\System32" },
        { OsKind.Windows, @"C:\Windows\...\..\System32", @"C:\Windows\System32" },
        { OsKind.Windows, @"C:\Windows \System32", @"C:\Windows \System32" },
        { OsKind.Windows, @"C:\Windows..\System32", @"C:\Windows..\System32" },
        { OsKind.Windows, @"C:\Windows\... ", @"C:\Windows" },
        { OsKind.Windows, @"C:\Windows\System32. . ", @"C:\Windows\System32" },
        { OsKind.Windows, @"C:\Windows.\System32", @"C:\Windows\System32" },
        { OsKind.Windows, @"C:\a\...\b", @"C:\a\...\b" },
        { OsKind.Windows, @"\\.\C:\Windows", @"C:\Windows" },
        { OsKind.Windows, @"\\?\UNC\server\share\x", @"\\server\share\x" },
        { OsKind.Linux, "/a. /b.", "/a. /b." },
        { OsKind.Linux, "/a//b/./c/../d/", "/a/b/d" },
        { OsKind.Linux, "/..", "/" },
        { OsKind.Linux, "/", "/" },
        { OsKind.MacOS, "/Users/test/../Shared/", "/Users/Shared" },
    };

    public static TheoryData<OsKind, string, string, string> CombineCases => new()
    {
        { OsKind.Windows, @"C:\Users\Test", @"Documents\My Games", @"C:\Users\Test\Documents\My Games" },
        { OsKind.Windows, @"C:\Users\Test", "a/b", @"C:\Users\Test\a\b" },
        { OsKind.Windows, @"C:\", "Games", @"C:\Games" },
        { OsKind.Linux, "/home/test", "a/b", "/home/test/a/b" },
        { OsKind.Linux, "/", "etc", "/etc" },
        { OsKind.MacOS, "/Users/test", "Library", "/Users/test/Library" },
    };

    public static TheoryData<string> StreamSpellings => new()
    {
        @"C:\Windows::$INDEX_ALLOCATION\System32",
        @"C:\Windows::$INDEX_ALLOCATION",
        @"C:\Users\Test::$INDEX_ALLOCATION",
        @"C:\Windows:$I30:$INDEX_ALLOCATION",
        @"C:\Windows\System32:stream",
        @"c:/Windows::$INDEX_ALLOCATION/System32",
        @"\\?\C:\Windows::$INDEX_ALLOCATION",
        @"\\server\share\a:b",
        @"\\server\share:x\a",
    };

    [Theory]
    [MemberData(nameof(StreamSpellings))]
    public void A_colon_after_the_drive_names_a_stream_and_is_not_an_absolute_path_on_windows(string path)
    {
        var windows = PathSyntax.For(OsKind.Windows);

        Assert.True(windows.UsesStreamSyntax(path));
        Assert.False(windows.IsAbsolute(path));
        Assert.Throws<ArgumentException>(() => windows.Normalize(path));
    }

    [Theory]
    [InlineData(OsKind.Windows, @"C:\Games\Sky")]
    [InlineData(OsKind.Windows, @"\\?\C:\Games")]
    [InlineData(OsKind.Windows, @"\\server\share\Games")]
    [InlineData(OsKind.Windows, "C:Games")]
    [InlineData(OsKind.Windows, @"Games:x")]
    [InlineData(OsKind.Linux, "/games/a:b")]
    [InlineData(OsKind.MacOS, "/Games/a:b")]
    public void Other_paths_do_not_use_stream_syntax(OsKind os, string path)
    {
        Assert.False(PathSyntax.For(os).UsesStreamSyntax(path));
    }

    [Theory]
    [InlineData(OsKind.Linux, "/games/a:b/c")]
    [InlineData(OsKind.MacOS, "/Games/a:b")]
    public void A_colon_is_an_ordinary_character_in_a_posix_folder_name(OsKind os, string path)
    {
        Assert.True(PathSyntax.For(os).IsAbsolute(path));
        Assert.Equal(path, PathSyntax.For(os).Normalize(path));
    }

    [Theory]
    [MemberData(nameof(AbsoluteCases))]
    public void IsAbsolute_follows_the_rules_of_the_given_os_on_any_host(OsKind os, string path, bool expected)
    {
        Assert.Equal(expected, PathSyntax.For(os).IsAbsolute(path));
    }

    [Theory]
    [MemberData(nameof(NormalizeCases))]
    public void Normalize_collapses_dots_and_separators_without_climbing_above_the_root(OsKind os, string path, string expected)
    {
        Assert.Equal(expected, PathSyntax.For(os).Normalize(path));
    }

    [Theory]
    [MemberData(nameof(CombineCases))]
    public void Combine_joins_with_the_os_separator(OsKind os, string basePath, string relative, string expected)
    {
        Assert.Equal(expected, PathSyntax.For(os).Combine(basePath, relative));
    }

    [Theory]
    [InlineData(OsKind.Windows, StringComparison.OrdinalIgnoreCase, '\\')]
    [InlineData(OsKind.MacOS, StringComparison.OrdinalIgnoreCase, '/')]
    [InlineData(OsKind.Linux, StringComparison.Ordinal, '/')]
    public void Comparison_and_separator_match_the_os(OsKind os, StringComparison comparison, char separator)
    {
        var syntax = PathSyntax.For(os);

        Assert.Equal(comparison, syntax.Comparison);
        Assert.Equal(separator, syntax.Separator);
    }

    [Fact]
    public void Normalize_rejects_a_relative_path()
    {
        Assert.Throws<ArgumentException>(() => PathSyntax.For(OsKind.Linux).Normalize("a/b"));
    }
}
