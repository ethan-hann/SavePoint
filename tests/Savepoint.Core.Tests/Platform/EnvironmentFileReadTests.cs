using Savepoint.Core.Platform;
using Savepoint.Core.Tests.Paths;

namespace Savepoint.Core.Tests.Platform;

// The real reader, on files in a temp folder.
public class EnvironmentFileReadTests
{
    [Fact]
    public void A_file_another_program_has_open_for_writing_can_still_be_read()
    {
        using var tree = new TempTree();
        var path = tree.File("libraryfolders.vdf", "\"libraryfolders\" { }");
        using var writer = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);

        var text = PlatformEnvironment.FromCurrentMachine().ReadTextFile(path);

        Assert.Equal("\"libraryfolders\" { }", text);
    }

    [Fact]
    public void A_missing_file_reads_as_null()
    {
        using var tree = new TempTree();

        Assert.Null(PlatformEnvironment.FromCurrentMachine().ReadTextFile(Path.Combine(tree.Root, "absent.vdf")));
    }
}
