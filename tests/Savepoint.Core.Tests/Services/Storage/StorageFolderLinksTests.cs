using Savepoint.Core.Errors;
using Savepoint.Core.Services.Storage;

namespace Savepoint.Core.Tests.Services.Storage;

// The paths a folder someone else prepared must keep inside it. The links themselves are covered through "Find the folder"
// (StorageFolderLinkTests); these cover the part that asks where a path really leads.
public class StorageFolderLinksTests
{
    private static TempFolder FolderWithOneGame(out string gitDir)
    {
        var temp = new TempFolder();
        gitDir = temp.Combine("storage", "game-one", "install.git");
        Directory.CreateDirectory(Path.Combine(gitDir, "hooks"));
        File.WriteAllText(Path.Combine(gitDir, "config"), "[core]\n");
        return temp;
    }

    [Fact]
    public void A_folder_whose_paths_all_stay_inside_it_is_not_refused()
    {
        using var temp = FolderWithOneGame(out _);

        Assert.Null(StorageFolderLinks.Refusal(temp.Combine("storage"), [], path => path));
    }

    [Fact]
    public void A_path_that_leads_outside_the_folder_is_refused_though_it_is_not_a_link()
    {
        using var temp = FolderWithOneGame(out var gitDir);
        var hooks = Path.Combine(gitDir, "hooks");

        var refusal = StorageFolderLinks.Refusal(temp.Combine("storage"), [], path => path == hooks ? temp.Combine("elsewhere", "hooks") : path);

        var error = Assert.IsType<StorageFolderHasLinkException>(refusal);
        Assert.Equal(hooks, error.Args["path"]);
    }

    [Fact]
    public void A_path_that_cannot_be_traced_is_refused_as_one_that_leaves_the_folder()
    {
        using var temp = FolderWithOneGame(out var gitDir);
        var config = Path.Combine(gitDir, "config");

        var refusal = StorageFolderLinks.Refusal(temp.Combine("storage"), [], path => path == config ? throw new IOException("a loop of links") : path);

        var error = Assert.IsType<StorageFolderHasLinkException>(refusal);
        Assert.Equal(config, error.Args["path"]);
    }

    [Fact]
    public void A_folder_that_is_missing_has_nothing_to_refuse()
    {
        using var temp = new TempFolder();

        Assert.Null(StorageFolderLinks.Refusal(temp.Combine("missing"), [], path => path));
    }
}
