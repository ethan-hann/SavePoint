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
