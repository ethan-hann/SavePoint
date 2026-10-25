using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;

namespace Savepoint.Core.Tests.Backend;

public class GamesQueryTests
{
    [Fact]
    public async Task Folder_status_lists_each_folder_as_stored_resolved_present_and_confirmed()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync();

        var statuses = (await workspace.Games.GetFolderStatusAsync(game.Id, workspace.Token)).Value!;

        Assert.Equal([RepoRole.Install, RepoRole.External], statuses.Select(s => s.Role));
        Assert.Equal(game.Install.Path, statuses[0].Path);
        Assert.Equal(game.Install.Path, statuses[0].ResolvedPath);
        Assert.All(statuses, status =>
        {
            Assert.True(status.Exists);
            Assert.True(status.Consent.Confirmed);
            Assert.True(Math.Abs((DateTimeOffset.UtcNow - status.Consent.ConfirmedAt!.Value).TotalMinutes) < 5);
        });
    }

    [Fact]
    public async Task Folder_status_of_a_game_without_saves_has_only_the_install_folder()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync(workspace.NewGame(withSaves: false));

        var statuses = (await workspace.Games.GetFolderStatusAsync(game.Id, workspace.Token)).Value!;

        Assert.Equal([RepoRole.Install], statuses.Select(s => s.Role));
    }

    [Fact]
    public async Task A_folder_that_moved_away_is_reported_gone_and_unconfirmed_while_the_other_is_untouched()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync();
        Directory.Move(game.External!.Path, game.External.Path + "-moved");

        var statuses = (await workspace.Games.GetFolderStatusAsync(game.Id, workspace.Token)).Value!;

        Assert.True(statuses[0].Exists);
        Assert.True(statuses[0].Consent.Confirmed);
        Assert.False(statuses[1].Exists);
        Assert.False(statuses[1].Consent.Confirmed);
        Assert.Null(statuses[1].Consent.ConfirmedAt);
    }

    [Fact]
    public async Task A_folder_with_a_token_this_machine_lacks_has_no_resolved_path()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync();
        await workspace.Backend.Services.Catalog.SaveGameAsync(game with { External = new ExternalLocation("{nowhere}/saves") }, workspace.Token);

        var statuses = (await workspace.Games.GetFolderStatusAsync(game.Id, workspace.Token)).Value!;

        Assert.Equal("{nowhere}/saves", statuses[1].Path);
        Assert.Null(statuses[1].ResolvedPath);
        Assert.False(statuses[1].Exists);
    }

    [Fact]
    public async Task Asking_about_an_unknown_game_is_an_error_not_an_empty_answer()
    {
        using var workspace = new BackendWorkspace();

        var statuses = await workspace.Games.GetFolderStatusAsync("nope", workspace.Token);
        var usage = await workspace.Games.GetStorageUsageAsync("nope", workspace.Token);

        Assert.False(statuses.Ok);
        Assert.False(usage.Ok);
    }

    [Fact]
    public async Task Storage_usage_counts_the_games_own_storage_and_reports_the_volume()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync();

        var usage = (await workspace.Games.GetStorageUsageAsync(game.Id, workspace.Token)).Value!;

        Assert.Equal(game.Id, usage.GameId);
        Assert.True(usage.BytesUsed > 0);
        Assert.True(usage.VolumeTotalBytes >= usage.VolumeFreeBytes);
        Assert.True(usage.VolumeFreeBytes > 0);
    }

    [Fact]
    public async Task Storage_usage_does_not_count_another_games_storage()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync(workspace.NewGame("Alpha"));
        var other = await workspace.AddGameAsync(workspace.NewGame("Beta"));
        File.WriteAllBytes(Path.Combine(workspace.DefaultStorageRoot, other.Id, "install.git", "padding.bin"), new byte[200_000]);

        var first = (await workspace.Games.GetStorageUsageAsync(game.Id, workspace.Token)).Value!;
        var second = (await workspace.Games.GetStorageUsageAsync(other.Id, workspace.Token)).Value!;

        Assert.True(second.BytesUsed >= first.BytesUsed + 200_000);
    }

    [Fact]
    public async Task Executables_are_listed_relative_to_the_install_folder()
    {
        using var workspace = new BackendWorkspace();
        var game = workspace.NewGame();

        var listed = await workspace.Games.ListExecutablesAsync(game.Install.Path, string.Empty, workspace.Token);

        Assert.Contains("game.exe", listed.Value!);
    }

    [Fact]
    public async Task Executables_are_found_through_a_path_token()
    {
        using var workspace = new BackendWorkspace();
        BackendWorkspace.WriteFile(Path.Combine(workspace.Home, "Games", "Tokened", "tokened.exe"), "MZ");

        var listed = await workspace.Games.ListExecutablesAsync("{userHome}/Games/Tokened", string.Empty, workspace.Token);

        Assert.Equal(["tokened.exe"], listed.Value!);
    }

    [Fact]
    public async Task The_process_name_suggested_is_the_game_program_not_the_launcher_beside_it()
    {
        using var workspace = new BackendWorkspace();
        var folder = Path.Combine(workspace.Home, "Games", "Alpha");
        Directory.CreateDirectory(Path.Combine(folder, "Alpha", "Binaries", "Win64"));
        File.WriteAllBytes(Path.Combine(folder, "AlphaLauncher.exe"), new byte[4_000_000]);
        File.WriteAllBytes(Path.Combine(folder, "Alpha.exe"), new byte[2_000]);
        File.WriteAllBytes(Path.Combine(folder, "Alpha", "Binaries", "Win64", "Alpha-Win64-Shipping.exe"), new byte[3_000_000]);

        var suggested = await workspace.Games.SuggestProcessNameAsync("{userHome}/Games/Alpha", "Alpha", workspace.Token);

        Assert.Equal("Alpha-Win64-Shipping.exe", suggested.Value);
    }

    [Fact]
    public async Task No_process_name_is_suggested_for_a_folder_that_is_not_there_or_holds_no_game()
    {
        using var workspace = new BackendWorkspace();
        BackendWorkspace.WriteFile(Path.Combine(workspace.Home, "Games", "Only", "Launcher.exe"), "MZ");

        var missing = await workspace.Games.SuggestProcessNameAsync(Path.Combine(workspace.Root, "nothing-here"), "Alpha", workspace.Token);
        var noGame = await workspace.Games.SuggestProcessNameAsync("{userHome}/Games/Only", "Only", workspace.Token);

        Assert.True(missing.Ok);
        Assert.Equal(string.Empty, missing.Value);
        Assert.Equal(string.Empty, noGame.Value);
    }

    [Fact]
    public async Task Executables_of_a_folder_that_is_not_there_are_an_empty_list()
    {
        using var workspace = new BackendWorkspace();

        var listed = await workspace.Games.ListExecutablesAsync(Path.Combine(workspace.Root, "nothing-here"), string.Empty, workspace.Token);

        Assert.Empty(listed.Value!);
    }

    [Fact]
    public async Task An_exported_game_imports_back_as_the_same_definition_and_saves_nothing()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync(workspace.NewGame(modHints: ["mods", "plugins.txt"], ignore: ["cache/"]));
        var file = Path.Combine(workspace.Root, "export", "alpha.json");

        var exported = await workspace.Games.ExportDefinitionAsync(game.Id, file, workspace.Token);
        var imported = await workspace.Games.ImportDefinitionAsync(file, workspace.Token);

        Assert.True(exported.Ok, exported.Error?.Detail);
        Assert.Equal(game, imported.Value);
        Assert.Single((await workspace.Games.ListAsync(workspace.Token)).Value!);
    }

    [Fact]
    public async Task A_file_that_is_not_json_is_refused()
    {
        using var workspace = new BackendWorkspace();
        var file = Path.Combine(workspace.Root, "broken.json");
        await File.WriteAllTextAsync(file, "this is not json", workspace.Token);

        var imported = await workspace.Games.ImportDefinitionAsync(file, workspace.Token);

        Assert.False(imported.Ok);
    }

    [Fact]
    public async Task A_definition_that_fails_validation_is_refused_with_its_issues()
    {
        using var workspace = new BackendWorkspace();
        var file = Path.Combine(workspace.Root, "no-name.json");
        await File.WriteAllTextAsync(file, """{ "schemaVersion": 1, "id": "x", "name": "", "install": { "path": "C:\\Games\\X" }, "launch": { "method": "exe", "exePath": "x.exe" } }""", workspace.Token);

        var imported = await workspace.Games.ImportDefinitionAsync(file, workspace.Token);

        Assert.Equal("error.validation.invalidPayload", imported.Error?.MessageId);
        Assert.Contains(
            (IEnumerable<ValidationIssue>)imported.Error!.Args["issues"]!,
            issue => issue.Path == "name");
    }

    [Fact]
    public async Task A_file_that_is_missing_is_an_error_not_an_exception()
    {
        using var workspace = new BackendWorkspace();

        var imported = await workspace.Games.ImportDefinitionAsync(Path.Combine(workspace.Root, "absent.json"), workspace.Token);

        Assert.False(imported.Ok);
    }

    [Fact]
    public async Task A_definition_without_an_id_gets_a_fresh_one()
    {
        using var workspace = new BackendWorkspace();
        var file = Path.Combine(workspace.Root, "no-id.json");
        await File.WriteAllTextAsync(file, """{ "schemaVersion": 1, "name": "Sky Game", "install": { "path": "C:\\Games\\Sky" }, "launch": { "method": "exe", "exePath": "sky.exe" } }""", workspace.Token);

        var imported = await workspace.Games.ImportDefinitionAsync(file, workspace.Token);

        Assert.StartsWith("sky-game-", imported.Value!.Id, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Exporting_an_unknown_game_writes_no_file()
    {
        using var workspace = new BackendWorkspace();
        var file = Path.Combine(workspace.Root, "ghost.json");

        var exported = await workspace.Games.ExportDefinitionAsync("ghost", file, workspace.Token);

        Assert.False(exported.Ok);
        Assert.False(File.Exists(file));
    }
}
