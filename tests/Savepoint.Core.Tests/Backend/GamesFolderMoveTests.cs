using Savepoint.Core.Catalog;

namespace Savepoint.Core.Tests.Backend;

// Pointing a folder that has history at another place is checked against what the history recorded (D31).
public class GamesFolderMoveTests
{
    private static string Unrelated(BackendWorkspace workspace, string name)
    {
        var folder = Path.Combine(workspace.GamesRoot, name);
        BackendWorkspace.WriteFile(Path.Combine(folder, "keep.ini"), "settings");
        return folder;
    }

    [Fact]
    public async Task A_saves_folder_with_history_pointed_at_a_folder_that_holds_none_of_its_files_needs_a_confirmation()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync();
        await workspace.CreateProfileAsync(game);
        var wrong = Unrelated(workspace, "wrong");

        var refused = await workspace.Games.UpdateAsync(game with { External = new ExternalLocation(wrong) });

        Assert.Equal("error.gameService.folderMismatch", refused.Error?.MessageId);
        Assert.Equal("external", refused.Error!.Args["role"]);
        Assert.Equal(wrong, refused.Error.Args["path"]);
        Assert.Equal(0, refused.Error.Args["presentCount"]);
        Assert.Equal(1, refused.Error.Args["recordedCount"]);
        Assert.Equal(game.External!.Path, (await workspace.Games.ListAsync(workspace.Token)).Value!.Single().External!.Path);
    }

    [Fact]
    public async Task The_confirmed_change_is_saved()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync();
        await workspace.CreateProfileAsync(game);
        var wrong = Unrelated(workspace, "wrong");

        var saved = await workspace.Games.UpdateAsync(game with { External = new ExternalLocation(wrong) }, confirmFolderChange: true);

        Assert.True(saved.Ok, saved.Error?.Detail);
        Assert.Equal(wrong, (await workspace.Games.ListAsync(workspace.Token)).Value!.Single().External!.Path);
    }

    [Fact]
    public async Task An_install_folder_pointed_at_the_wrong_place_is_checked_the_same_way()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync();
        await workspace.CreateProfileAsync(game);

        var refused = await workspace.Games.UpdateAsync(game with { Install = new InstallLocation(Unrelated(workspace, "elsewhere")) });

        Assert.Equal("error.gameService.folderMismatch", refused.Error?.MessageId);
        Assert.Equal("install", refused.Error!.Args["role"]);
        Assert.True((int)refused.Error.Args["recordedCount"]! >= 2);
    }

    [Fact]
    public async Task A_folder_moved_with_its_files_is_accepted_without_a_question()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync();
        await workspace.CreateProfileAsync(game);
        var moved = game.External!.Path + "-moved";
        Directory.Move(game.External.Path, moved);

        var saved = await workspace.Games.UpdateAsync(game with { External = new ExternalLocation(moved) });

        Assert.True(saved.Ok, saved.Error?.Detail);
    }

    [Fact]
    public async Task Files_the_ignore_list_names_do_not_count_against_a_folder_pointed_somewhere_else()
    {
        using var workspace = new BackendWorkspace();
        var game = workspace.NewGame();
        foreach (var name in new[] { "a.log", "b.log", "c.log", "d.log" })
        {
            BackendWorkspace.WriteFile(Path.Combine(game.External!.Path, "logs", name), "a log");
        }

        game = await workspace.AddGameAsync(game);
        await workspace.CreateProfileAsync(game);

        // The new folder holds the one file the point records that the ignore list does not name; the four logs stayed behind.
        var moved = Path.Combine(workspace.GamesRoot, "moved-saves");
        BackendWorkspace.WriteFile(Path.Combine(moved, "slot1.sav"), "save one");

        var saved = await workspace.Games.UpdateAsync(game with { External = new ExternalLocation(moved), IgnorePaths = ["logs/"] });

        Assert.True(saved.Ok, saved.Error?.Detail);
        Assert.Equal(moved, (await workspace.Games.ListAsync(workspace.Token)).Value!.Single().External!.Path);
    }

    [Fact]
    public async Task A_folder_with_no_saved_points_can_be_pointed_anywhere()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync();

        var saved = await workspace.Games.UpdateAsync(game with { External = new ExternalLocation(Unrelated(workspace, "fresh")) });

        Assert.True(saved.Ok, saved.Error?.Detail);
    }

    [Fact]
    public async Task Editing_something_else_asks_nothing_about_folders()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync();
        await workspace.CreateProfileAsync(game);

        var saved = await workspace.Games.UpdateAsync(game with { Name = "Renamed" });

        Assert.True(saved.Ok, saved.Error?.Detail);
    }
}
