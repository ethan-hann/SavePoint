using System.Runtime.InteropServices;
using Savepoint.Core.Paths;

namespace Savepoint.Core.Tests.Paths;

// These use the real file system of the machine running the tests; links stay inside the temp tree.
public class RealPathHostTests
{
    [Fact]
    public void A_directory_link_in_the_middle_of_a_path_is_followed_to_its_target()
    {
        using var tree = new TempTree();
        tree.File(Path.Combine("real", "sub", "file.txt"));
        var link = tree.DirectoryLink("link", Path.Combine(tree.Root, "real"));

        var resolved = RealPath.Resolve(Path.Combine(link, "sub", "file.txt"));

        Assert.Equal(Path.Combine(tree.Root, "real", "sub", "file.txt"), resolved);
    }

    [Fact]
    public void A_missing_tail_below_an_existing_folder_is_kept_as_written()
    {
        using var tree = new TempTree();
        var path = Path.Combine(tree.Root, "not", "created", "yet");

        Assert.Equal(path, RealPath.Resolve(path));
    }

    [Fact]
    public void A_missing_tail_below_a_link_is_appended_to_the_links_target()
    {
        using var tree = new TempTree();
        var real = tree.Dir("real");
        var link = tree.DirectoryLink("link", real);

        Assert.Equal(Path.Combine(real, "not", "yet"), RealPath.Resolve(Path.Combine(link, "not", "yet")));
    }

    [Fact]
    public void A_dangling_link_resolves_to_the_text_of_its_target()
    {
        using var tree = new TempTree();
        var target = tree.Dir("gone");
        var link = tree.DirectoryLink("dangling", target);
        Directory.Delete(target);

        Assert.Equal(Path.Combine(target, "file"), RealPath.Resolve(Path.Combine(link, "file")));
    }

    [Fact]
    public void A_chain_of_two_links_is_followed_to_the_end()
    {
        using var tree = new TempTree();
        var real = tree.Dir("real");
        var first = tree.DirectoryLink("first", real);
        var second = tree.DirectoryLink("second", first);

        Assert.Equal(Path.Combine(real, "x"), RealPath.Resolve(Path.Combine(second, "x")));
    }

    [Fact]
    public void A_relative_symlink_is_taken_from_its_own_folder()
    {
        using var tree = new TempTree();
        var real = tree.Dir("a", "real");
        var link = tree.Symlink(Path.Combine("a", "link"), "real", isDirectory: true);

        Assert.Equal(Path.Combine(real, "x"), RealPath.Resolve(Path.Combine(link, "x")));
    }

    [Fact]
    public void A_symlink_to_a_file_resolves_to_the_file()
    {
        using var tree = new TempTree();
        var file = tree.File("real.txt");
        var link = tree.Symlink("link.txt", file, isDirectory: false);

        Assert.Equal(file, RealPath.Resolve(link));
    }

    [Fact]
    public void A_relative_path_is_resolved_against_the_current_directory()
    {
        var expected = Path.Combine(RealPath.Resolve(Environment.CurrentDirectory), "somewhere", "else");

        Assert.Equal(expected, RealPath.Resolve(Path.Combine("somewhere", "else")));
    }

    [Fact]
    public void A_windows_short_name_is_expanded_to_the_long_name()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Short (8.3) names exist only on Windows.");
        using var tree = new TempTree();
        var longPath = tree.Dir("LongDirectoryNameForShortNames");
        var shortPath = ShortPathOf(longPath);
        Assert.SkipWhen(string.Equals(shortPath, longPath, StringComparison.OrdinalIgnoreCase), "This volume has 8.3 names turned off.");

        Assert.Equal(Path.Combine(longPath, "x"), RealPath.Resolve(Path.Combine(shortPath, "x")));
    }

    private static string ShortPathOf(string path)
    {
        var buffer = new char[1024];
        var length = GetShortPathName(path, buffer, (uint)buffer.Length);
        return length == 0 ? path : new string(buffer, 0, (int)length);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetShortPathName(string longPath, char[] shortPath, uint bufferLength);
}
