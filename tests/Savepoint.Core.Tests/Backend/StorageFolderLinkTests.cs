using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Tests.Git.Engine;

namespace Savepoint.Core.Tests.Backend;

// "Find the folder" adopts a folder somebody else may have prepared. It empties hooks folders and rewrites configs in what it adopts,
// and through a link that would be done to files that were never SavePoint's, so a link anywhere in what it reads or writes refuses
// the whole folder before anything is touched.
public class StorageFolderLinkTests
{
    private const string _linkRefusal = "error.storage.folderHasLink";

    // A game with history and its storage folder carried away, as a drive that now has another letter would be.
    private static async Task<(BackendWorkspace Workspace, GameDefinition Game, string Away)> WithStorageGoneAsync()
    {
        var workspace = new BackendWorkspace();
        try
        {
            await workspace.AppStatus.CheckAsync(workspace.Token);
            var game = await workspace.AddGameAsync();
            await workspace.CreateProfileAsync(game);
            await workspace.WaitForIdleAsync(TimeSpan.FromSeconds(60));
            var away = workspace.DefaultStorageRoot + "-away";
            Directory.Move(workspace.DefaultStorageRoot, away);
            workspace.Restart();
            return (workspace, game, away);
        }
        catch
        {
            workspace.Dispose();
            throw;
        }
    }

    // Somebody's own files, which SavePoint has no business with.
    private static string SomebodysFolder(BackendWorkspace workspace, string name)
    {
        var folder = Path.Combine(workspace.Root, name);
        BackendWorkspace.WriteFile(Path.Combine(folder, "keep.txt"), "keep");
        BackendWorkspace.WriteFile(Path.Combine(folder, "sub", "file.txt"), "inside");
        return folder;
    }

    private static void LinkOrSkip(string link, string target)
    {
        if (!TestLinks.TryCreateFolderLink(link, target))
        {
            Assert.Skip("this account cannot create folder links");
        }
    }

    private static void CopyFolder(string from, string to)
    {
        foreach (var directory in Directory.EnumerateDirectories(from, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(to, Path.GetRelativePath(from, directory)));
        }

        Directory.CreateDirectory(to);
        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            File.Copy(file, Path.Combine(to, Path.GetRelativePath(from, file)));
        }
    }

    // A copy of the carried-away history with a second game folder beside the real one, under any name.
    private static string PreparedWithExtraCopy(BackendWorkspace workspace, GameDefinition game, string away)
    {
        var prepared = Path.Combine(workspace.Root, "prepared");
        CopyFolder(away, prepared);
        CopyFolder(Path.Combine(prepared, game.Id, "install.git"), Path.Combine(prepared, "other", "install.git"));
        return prepared;
    }

    private static async Task AssertRefusedUntouchedAsync(BackendWorkspace workspace, OperationResult<string> found, string linkPath, string prepared, IReadOnlyDictionary<string, string> preparedBefore)
    {
        Assert.Equal(_linkRefusal, found.Error?.MessageId);
        Assert.Equal(linkPath, found.Error!.Args["path"]);
        Assert.Equal(preparedBefore, BackendWorkspace.Snapshot(prepared));
        Assert.Equal(workspace.DefaultStorageRoot, (await workspace.Settings.GetAsync(workspace.Token)).Value!.StorageRoot);
        Assert.False(Directory.Exists(workspace.DefaultStorageRoot), "a refused folder must not make an empty one beside it");
    }

    [Fact(Timeout = 120_000)]
    public async Task A_hooks_folder_that_is_a_link_refuses_the_folder_and_the_files_it_leads_to_are_left_alone()
    {
        var (workspace, game, away) = await WithStorageGoneAsync();
        using var _ = workspace;
        var somebodys = SomebodysFolder(workspace, "somebodys-saves");
        var prepared = PreparedWithExtraCopy(workspace, game, away);
        var hooks = Path.Combine(prepared, "other", "install.git", "hooks");
        if (Directory.Exists(hooks))
        {
            Directory.Delete(hooks, recursive: true);
        }

        LinkOrSkip(hooks, somebodys);
        try
        {
            var before = BackendWorkspace.Snapshot(somebodys);
            var preparedBefore = BackendWorkspace.Snapshot(prepared);

            var found = await workspace.AppStatus.UseStorageFolderAsync(prepared, workspace.Token);

            await AssertRefusedUntouchedAsync(workspace, found, hooks, prepared, preparedBefore);
            Assert.Equal(before, BackendWorkspace.Snapshot(somebodys));
            Assert.True(Directory.Exists(Path.Combine(somebodys, "sub")));
        }
        finally
        {
            TestLinks.RemoveFolderLink(hooks);
        }
    }

    [Fact(Timeout = 120_000)]
    public async Task A_git_directory_that_is_a_link_refuses_the_folder_and_the_config_it_leads_to_is_not_rewritten()
    {
        var (workspace, game, away) = await WithStorageGoneAsync();
        using var _ = workspace;
        var linked = Path.Combine(workspace.Root, "somebodys-git");
        BackendWorkspace.WriteFile(Path.Combine(linked, "config"), "[core]\n\tfsmonitor = echo hello\n");
        var prepared = Path.Combine(workspace.Root, "prepared");
        CopyFolder(away, prepared);
        var gitDir = Path.Combine(prepared, "other", "install.git");
        Directory.CreateDirectory(Path.GetDirectoryName(gitDir)!);
        LinkOrSkip(gitDir, linked);
        try
        {
            var before = BackendWorkspace.Snapshot(linked);
            var preparedBefore = BackendWorkspace.Snapshot(prepared);

            var found = await workspace.AppStatus.UseStorageFolderAsync(prepared, workspace.Token);

            await AssertRefusedUntouchedAsync(workspace, found, gitDir, prepared, preparedBefore);
            Assert.Equal(before, BackendWorkspace.Snapshot(linked));
        }
        finally
        {
            TestLinks.RemoveFolderLink(gitDir);
        }
    }

    [Fact(Timeout = 120_000)]
    public async Task A_game_folder_that_is_a_link_refuses_the_folder_even_when_it_is_the_one_that_holds_the_history()
    {
        var (workspace, game, away) = await WithStorageGoneAsync();
        using var _ = workspace;
        var prepared = Path.Combine(workspace.Root, "prepared");
        Directory.CreateDirectory(prepared);
        var gameFolder = Path.Combine(prepared, game.Id);
        LinkOrSkip(gameFolder, Path.Combine(away, game.Id));
        try
        {
            var preparedBefore = BackendWorkspace.Snapshot(prepared);

            var found = await workspace.AppStatus.UseStorageFolderAsync(prepared, workspace.Token);

            await AssertRefusedUntouchedAsync(workspace, found, gameFolder, prepared, preparedBefore);
        }
        finally
        {
            TestLinks.RemoveFolderLink(gameFolder);
        }
    }

    [Fact(Timeout = 120_000)]
    public async Task A_listed_games_folder_that_is_a_link_to_a_plain_folder_refuses_the_folder_so_no_history_is_made_inside_it()
    {
        using var workspace = new BackendWorkspace();
        await workspace.AppStatus.CheckAsync(workspace.Token);
        var first = await workspace.AddGameAsync(workspace.NewGame("First"));
        var second = await workspace.AddGameAsync(workspace.NewGame("Second"));
        await workspace.CreateProfileAsync(first);
        await workspace.CreateProfileAsync(second);
        await workspace.WaitForIdleAsync(TimeSpan.FromSeconds(60));
        var away = workspace.DefaultStorageRoot + "-away";
        Directory.Move(workspace.DefaultStorageRoot, away);
        workspace.Restart();
        var prepared = Path.Combine(workspace.Root, "prepared");
        CopyFolder(Path.Combine(away, first.Id), Path.Combine(prepared, first.Id));
        var somebodys = SomebodysFolder(workspace, "somebodys-saves");
        var secondFolder = Path.Combine(prepared, second.Id);
        LinkOrSkip(secondFolder, somebodys);
        try
        {
            var before = BackendWorkspace.Snapshot(somebodys);
            var preparedBefore = BackendWorkspace.Snapshot(prepared);

            var found = await workspace.AppStatus.UseStorageFolderAsync(prepared, workspace.Token);

            await AssertRefusedUntouchedAsync(workspace, found, secondFolder, prepared, preparedBefore);
            Assert.Equal(before, BackendWorkspace.Snapshot(somebodys));
            Assert.False(Directory.Exists(Path.Combine(somebodys, "install.git")), "a repository was made inside a folder SavePoint does not own");
        }
        finally
        {
            TestLinks.RemoveFolderLink(secondFolder);
        }
    }

    // SavePoint writes into a git directory at every save and sweeps it at every start; a link in any folder of it, however deep, leads
    // those deletes and writes out of the folder.
    [Theory(Timeout = 120_000)]
    [InlineData("refs/heads")]
    [InlineData("refs/heads/team")]
    [InlineData("objects/pack")]
    public async Task A_folder_deeper_in_a_git_directory_that_is_a_link_refuses_the_folder_and_the_files_it_leads_to_are_left_alone(string linked)
    {
        var (workspace, game, away) = await WithStorageGoneAsync();
        using var _ = workspace;
        var somebodys = SomebodysFolder(workspace, "somebodys-saves");
        BackendWorkspace.WriteFile(Path.Combine(somebodys, "keep.lock"), "keep");
        BackendWorkspace.WriteFile(Path.Combine(somebodys, "sub", "also.lock"), "keep");
        var prepared = PreparedWithExtraCopy(workspace, game, away);
        var link = Path.Combine(prepared, "other", "install.git", linked.Replace('/', Path.DirectorySeparatorChar));
        if (Directory.Exists(link))
        {
            Directory.Delete(link, recursive: true);
        }

        LinkOrSkip(link, somebodys);
        try
        {
            var before = BackendWorkspace.Snapshot(somebodys);
            var preparedBefore = BackendWorkspace.Snapshot(prepared);

            var found = await workspace.AppStatus.UseStorageFolderAsync(prepared, workspace.Token);

            await AssertRefusedUntouchedAsync(workspace, found, link, prepared, preparedBefore);
            Assert.Equal(before, BackendWorkspace.Snapshot(somebodys));
        }
        finally
        {
            TestLinks.RemoveFolderLink(link);
        }
    }

    // A pointer file redirects git without being a link: git then takes the repository's refs, or its objects, from somewhere else.
    [Theory(Timeout = 120_000)]
    [InlineData("commondir", "../../elsewhere")]
    [InlineData("objects/info/alternates", "/somebodys/other/objects")]
    [InlineData("objects/info/alternates", "# an earlier one\n\n../../elsewhere/objects\n")]
    public async Task A_git_directory_that_points_git_at_another_place_refuses_the_folder(string file, string content)
    {
        var (workspace, game, away) = await WithStorageGoneAsync();
        using var _ = workspace;
        var prepared = PreparedWithExtraCopy(workspace, game, away);
        var pointer = Path.Combine(prepared, "other", "install.git", file.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(pointer)!);
        File.WriteAllText(pointer, content);
        var preparedBefore = BackendWorkspace.Snapshot(prepared);

        var found = await workspace.AppStatus.UseStorageFolderAsync(prepared, workspace.Token);

        await AssertRefusedUntouchedAsync(workspace, found, pointer, prepared, preparedBefore);
    }

    // An alternates file with nothing but comments and blank lines names no path.
    [Fact(Timeout = 120_000)]
    public async Task An_alternates_file_that_names_no_path_does_not_refuse_the_folder()
    {
        var (workspace, game, away) = await WithStorageGoneAsync();
        using var _ = workspace;
        var prepared = PreparedWithExtraCopy(workspace, game, away);
        var alternates = Path.Combine(prepared, "other", "install.git", "objects", "info", "alternates");
        Directory.CreateDirectory(Path.GetDirectoryName(alternates)!);
        File.WriteAllText(alternates, "# nothing here\n\n   \n");

        var found = await workspace.AppStatus.UseStorageFolderAsync(prepared, workspace.Token);

        Assert.True(found.Ok, found.Error?.Detail);
        Assert.Equal(prepared, found.Value);
    }

    [Fact(Timeout = 120_000)]
    public async Task A_config_file_that_is_a_link_refuses_the_folder()
    {
        var (workspace, game, away) = await WithStorageGoneAsync();
        using var _ = workspace;
        var prepared = PreparedWithExtraCopy(workspace, game, away);
        var config = Path.Combine(prepared, "other", "install.git", "config");
        var target = Path.Combine(workspace.Root, "somebodys-config");
        File.Move(config, target);
        try
        {
            File.CreateSymbolicLink(config, target);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            Assert.Skip("this account cannot create file links");
        }

        var before = File.ReadAllText(target);
        var preparedBefore = BackendWorkspace.Snapshot(prepared);

        var found = await workspace.AppStatus.UseStorageFolderAsync(prepared, workspace.Token);

        await AssertRefusedUntouchedAsync(workspace, found, config, prepared, preparedBefore);
        Assert.Equal(before, File.ReadAllText(target));
    }

    [Fact(Timeout = 120_000)]
    public async Task A_folder_with_no_links_in_it_is_still_found()
    {
        var (workspace, game, away) = await WithStorageGoneAsync();
        using var _ = workspace;
        var prepared = PreparedWithExtraCopy(workspace, game, away);

        var found = await workspace.AppStatus.UseStorageFolderAsync(prepared, workspace.Token);

        Assert.True(found.Ok, found.Error?.Detail);
        Assert.Equal(prepared, found.Value);
    }
}
