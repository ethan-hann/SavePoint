using Savepoint.Core.Catalog;

namespace Savepoint.Core.Tests.Backend;

public class CapturePreviewTests
{
    [Fact]
    public async Task Before_anything_is_saved_everything_is_new()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync();

        var preview = (await workspace.Games.GetCapturePreviewAsync(game.Id, workspace.Token)).Value!;

        Assert.Equal(game.Id, preview.GameId);
        var install = preview.Folders.Single(f => f.Role == RepoRole.Install);
        var saves = preview.Folders.Single(f => f.Role == RepoRole.External);
        Assert.Equal(game.Install.Path, install.Path);
        Assert.Equal(2, install.FileCount);
        Assert.Equal("MZ not a real program".Length + "base content".Length, install.Bytes);
        Assert.Equal(0, install.ModFileCount);
        Assert.Equal(game.External!.Path, saves.Path);
        Assert.Equal(1, saves.FileCount);
        Assert.Equal(0, saves.ModFileCount);
        Assert.Equal(install.Bytes + saves.Bytes, preview.NewBytesEstimate);
    }

    [Fact]
    public async Task Mod_files_are_the_install_files_under_the_mod_hints_and_the_consent_marker_is_not_counted()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync(workspace.NewGame(withSaves: false, modHints: ["mods", "plugins.txt"]));
        BackendWorkspace.WriteFile(Path.Combine(game.Install.Path, "mods", "a.esp"), "a");
        BackendWorkspace.WriteFile(Path.Combine(game.Install.Path, "mods", "deep", "b.esp"), "b");
        BackendWorkspace.WriteFile(Path.Combine(game.Install.Path, "plugins.txt"), "list");

        var install = (await workspace.Games.GetCapturePreviewAsync(game.Id, workspace.Token)).Value!.Folders.Single();

        Assert.Equal(3, install.ModFileCount);
        Assert.Equal(5, install.FileCount);
    }

    [Fact]
    public async Task Ignored_files_are_left_out_of_the_counts()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync(workspace.NewGame(withSaves: false, ignore: ["cache/"]));
        BackendWorkspace.WriteFile(Path.Combine(game.Install.Path, "cache", "tmp.bin"), "cache");

        var install = (await workspace.Games.GetCapturePreviewAsync(game.Id, workspace.Token)).Value!.Folders.Single();

        Assert.Equal(2, install.FileCount);
    }

    [Fact]
    public async Task What_a_profile_already_holds_costs_nothing_new()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync();
        await workspace.CreateProfileAsync(game);
        BackendWorkspace.WriteFile(Path.Combine(game.Install.Path, "data", "patch.pak"), "twelve bytes");

        var preview = (await workspace.Games.GetCapturePreviewAsync(game.Id, workspace.Token)).Value!;

        Assert.Equal("twelve bytes".Length, preview.NewBytesEstimate);
        Assert.Equal(3, preview.Folders.Single(f => f.Role == RepoRole.Install).FileCount);
    }

    [Fact]
    public async Task A_preview_writes_nothing_into_the_game_folders()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync(workspace.NewGame(ignore: ["cache/"]));
        var installBefore = BackendWorkspace.Snapshot(game.Install.Path);
        var savesBefore = BackendWorkspace.Snapshot(game.External!.Path);

        await workspace.Games.GetCapturePreviewAsync(game.Id, workspace.Token);

        Assert.Equal(installBefore, BackendWorkspace.Snapshot(game.Install.Path));
        Assert.Equal(savesBefore, BackendWorkspace.Snapshot(game.External.Path));
    }

    [Fact]
    public async Task A_folder_that_is_gone_makes_the_preview_fail_naming_the_folder()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync();
        Directory.Move(game.External!.Path, game.External.Path + "-gone");

        var preview = await workspace.Games.GetCapturePreviewAsync(game.Id, workspace.Token);

        Assert.Equal("error.path.missing", preview.Error?.MessageId);
        Assert.Equal("external", preview.Error!.Args["role"]);
    }

    [Fact]
    public async Task A_preview_of_an_unknown_game_is_an_error()
    {
        using var workspace = new BackendWorkspace();

        Assert.False((await workspace.Games.GetCapturePreviewAsync("nope", workspace.Token)).Ok);
    }
}
