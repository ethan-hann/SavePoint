using Savepoint.Core.Paths;
using Savepoint.Core.Platform;

namespace Savepoint.Core.Tests.Platform;

// The fixtures copy the shape of a real libraryfolders.vdf: tab-separated, LF lines, backslashes doubled, and an offline library.
public class SteamLibraryFileTests
{
    private static readonly PathSyntax _windows = PathSyntax.For(OsKind.Windows);
    private static readonly PathSyntax _linux = PathSyntax.For(OsKind.Linux);

    [Fact]
    public void Every_library_in_a_real_windows_file_is_listed_in_order_including_the_offline_one()
    {
        var paths = SteamLibraryFile.LibraryPaths(SteamFixtures.WindowsFile, _windows);

        Assert.Equal([@"C:\Program Files (x86)\Steam", @"D:\SteamLibrary", @"X:\SteamLibrary", @"N:\SteamLibrary"], paths);
    }

    [Fact]
    public void Windows_line_endings_do_not_change_the_answer()
    {
        var paths = SteamLibraryFile.LibraryPaths(SteamFixtures.WindowsFile.Replace("\n", "\r\n"), _windows);

        Assert.Equal(4, paths.Count);
    }

    [Fact]
    public void A_linux_file_lists_its_libraries()
    {
        var paths = SteamLibraryFile.LibraryPaths(SteamFixtures.LinuxFile, _linux);

        Assert.Equal(["/home/test/.local/share/Steam", "/mnt/games/SteamLibrary"], paths);
    }

    [Fact]
    public void The_older_layout_with_a_path_as_the_value_of_each_number_is_read()
    {
        var paths = SteamLibraryFile.LibraryPaths(SteamFixtures.OldLayoutFile, _windows);

        Assert.Equal([@"F:\SteamLibrary", @"E:\Games\Steam"], paths);
    }

    [Fact]
    public void Entries_that_are_not_absolute_paths_are_dropped()
    {
        var text = SteamFixtures.File(@"""0""{""path""""relative\\folder""}""1""{""path""""""}""2""{""path""""D:\\Kept""}""3""{""label""""no path""}");

        Assert.Equal([@"D:\Kept"], SteamLibraryFile.LibraryPaths(text, _windows));
    }

    [Fact]
    public void A_path_of_the_other_operating_systems_style_is_dropped()
    {
        Assert.Empty(SteamLibraryFile.LibraryPaths(SteamFixtures.WindowsFile, _linux));
    }

    [Fact]
    public void Paths_are_normalized_the_way_the_table_expects_them()
    {
        var text = SteamFixtures.File(@"""0""{""path""""d:/Steam Library//""}");

        Assert.Equal([@"D:\Steam Library"],SteamLibraryFile.LibraryPaths(text, _windows));
    }

    [Fact]
    public void An_escaped_quote_and_a_comment_do_not_derail_the_reader()
    {
        var text = "// header\n\"libraryfolders\"\n{\n\t\"0\"\n\t{\n\t\t\"label\"\t\t\"The \\\"big\\\" one\"\n\t\t\"path\"\t\t\"D:\\\\Big\" // trailing\n\t}\n}\n";

        Assert.Equal([@"D:\Big"], SteamLibraryFile.LibraryPaths(text, _windows));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \n")]
    [InlineData("not a vdf file at all")]
    [InlineData("\"libraryfolders\"")]
    [InlineData("\"libraryfolders\"\n{\n\t\"0\"\n\t{\n\t\t\"path\"\t\t\"D:\\\\Cut")]
    [InlineData("\"libraryfolders\"\n{\n\t\"0\"\n\t{\n\t\t\"path\"\t\t\"D:\\\\Open\"\n")]
    [InlineData("\"libraryfolders\" { \"0\" { \"path\" \"D:\\\\Stray\" } } }")]
    [InlineData("\"other\" { \"0\" { \"path\" \"D:\\\\Wrong root\" } }")]
    [InlineData("\0\0\0\0")]
    public void A_missing_empty_or_malformed_file_gives_no_libraries_and_does_not_throw(string? text)
    {
        Assert.Empty(SteamLibraryFile.LibraryPaths(text, _windows));
    }

    [Fact]
    public void A_file_nested_far_deeper_than_steam_writes_it_is_refused_whole()
    {
        var deep = string.Concat(Enumerable.Repeat("\"k\" { ", 200)) + string.Concat(Enumerable.Repeat("} ", 200));
        var text = SteamFixtures.File("\"0\" { \"path\" \"D:\\\\Kept\" }\n" + deep);

        Assert.Empty(SteamLibraryFile.LibraryPaths(text, _windows));
    }

    [Fact]
    public void Nesting_that_would_overflow_the_stack_is_refused_without_crashing()
    {
        var text = "\"libraryfolders\" " + string.Concat(Enumerable.Repeat("\"k\" { ", 70000)) + string.Concat(Enumerable.Repeat("} ", 70000));

        Assert.Empty(SteamLibraryFile.LibraryPaths(text, _windows));
    }

    [Fact]
    public void A_runaway_list_is_capped()
    {
        var entries = string.Concat(Enumerable.Range(0, 5000).Select(i => $"\"{i}\" {{ \"path\" \"D:\\\\Lib{i}\" }} "));

        var paths = SteamLibraryFile.LibraryPaths(SteamFixtures.File(entries), _windows);

        Assert.InRange(paths.Count, 1, 512);
    }
}
