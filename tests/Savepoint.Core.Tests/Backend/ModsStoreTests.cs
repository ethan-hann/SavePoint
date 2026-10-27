using System.Text.Json;
using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;
using Savepoint.Core.Paths;

namespace Savepoint.Core.Tests.Backend;

// The mods folder is the third folder of a game: its own store, its own consent marker, and every check the other two get.
public class ModsStoreTests
{
    [Fact]
    public async Task Adding_a_game_with_a_mods_folder_makes_three_stores_and_marks_all_three_folders()
    {
        using var workspace = new BackendWorkspace();
        var definition = workspace.NewGameWithMods();

        var game = await workspace.AddGameAsync(definition);

        Assert.True(Directory.Exists(workspace.StoreOf(game, "install.git")));
        Assert.True(Directory.Exists(workspace.StoreOf(game, "external.git")));
        Assert.True(Directory.Exists(workspace.StoreOf(game, "mods.git")));
        var marker = Assert.IsType<ConsentMarkerState.Valid>(ConsentMarker.Read(game.Mods!.Path));
        Assert.Equal((game.Id, RepoRole.Mods), (marker.Marker.GameId, marker.Marker.Role));
        using var file = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(game.Mods.Path, ConsentMarker.FileName), workspace.Token));
        Assert.Equal("mods", file.RootElement.GetProperty("role").GetString());
        Assert.Equal(definition.Mods, (await workspace.Games.ListAsync(workspace.Token)).Value!.Single().Mods);
    }

    [Fact]
    public async Task A_game_without_a_mods_folder_gets_no_mods_store()
    {
        using var workspace = new BackendWorkspace();

        var game = await workspace.AddGameAsync();

        Assert.False(Directory.Exists(workspace.StoreOf(game, "mods.git")));
    }

    [Fact]
    public async Task The_folder_status_lists_the_game_folder_the_mods_folder_and_the_saves_folder()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync(workspace.NewGameWithMods());

        var folders = (await workspace.Games.GetFolderStatusAsync(game.Id, workspace.Token)).Value!;

        Assert.Equal([RepoRole.Install, RepoRole.Mods, RepoRole.External], folders.Select(folder => folder.Role));
        var mods = folders.Single(folder => folder.Role == RepoRole.Mods);
        Assert.Equal(game.Mods!.Path, mods.ResolvedPath);
        Assert.True(mods.Exists);
        Assert.True(mods.Consent.Confirmed);
    }

    [Fact]
    public async Task A_mods_folder_inside_the_saves_folder_is_refused_on_the_mods_field_even_when_a_token_hides_it()
    {
        using var workspace = new BackendWorkspace();
        var saves = Path.Combine(workspace.Home, "data");
        Directory.CreateDirectory(saves);
        var definition = workspace.NewGame(withSaves: false) with
        {
            External = new ExternalLocation(saves),
            Mods = new ModsLocation("{userHome}/data/Mods"),
        };

        var added = await workspace.Games.AddAsync(definition);

        Assert.Equal("error.validation.invalidPayload", added.Error?.MessageId);
        var issue = Assert.Single(Assert.IsAssignableFrom<IEnumerable<ValidationIssue>>(added.Error!.Args["issues"]));
        Assert.Equal(("mods.path", ValidationMessageIds.GamePathsNested), (issue.Path, issue.MessageId));
        Assert.Empty((await workspace.Games.ListAsync(workspace.Token)).Value!);
    }

    [Fact]
    public async Task A_mods_folder_that_overlaps_another_games_folder_is_refused_on_the_mods_role()
    {
        using var workspace = new BackendWorkspace();
        var first = await workspace.AddGameAsync(workspace.NewGame("Alpha"));
        var second = workspace.NewGame("Beta", withSaves: false) with { Mods = new ModsLocation(first.External!.Path) };

        var added = await workspace.Games.AddAsync(second);

        Assert.Equal("error.gameService.pathOverlap", added.Error?.MessageId);
        Assert.Equal(first.Id, added.Error!.Args["conflictingGameId"]);
        Assert.Equal("mods", added.Error.Args["role"]);
        Assert.IsType<ConsentMarkerState.Valid>(ConsentMarker.Read(first.External.Path));
    }

    [Fact]
    public async Task Another_games_install_folder_over_a_mods_folder_is_refused_with_that_games_name()
    {
        using var workspace = new BackendWorkspace();
        var first = await workspace.AddGameAsync(workspace.NewGameWithMods("Alpha"));
        var second = workspace.NewGame("Beta", withSaves: false) with { Install = new InstallLocation(Path.Combine(first.Mods!.Path, "modA")) };

        var added = await workspace.Games.AddAsync(second);

        Assert.Equal("error.gameService.pathOverlap", added.Error?.MessageId);
        Assert.Equal("Alpha", added.Error!.Args["conflictingGameName"]);
        Assert.Equal("install", added.Error.Args["role"]);
    }

    [Fact]
    public async Task A_mods_folder_can_be_added_to_a_game_later_and_gets_its_own_store_and_marker()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync();
        var withMods = workspace.NewGameWithMods("Other") with { Id = game.Id, Name = game.Name, Install = game.Install, External = game.External, Launch = game.Launch };

        var updated = await workspace.Games.UpdateAsync(withMods);

        Assert.True(updated.Ok, updated.Error?.Detail);
        Assert.True(Directory.Exists(workspace.StoreOf(game, "mods.git")));
        Assert.Equal(game.Id, Assert.IsType<ConsentMarkerState.Valid>(ConsentMarker.Read(withMods.Mods!.Path)).Marker.GameId);
    }

    [Fact]
    public async Task A_mods_folder_with_history_pointed_at_a_folder_holding_none_of_its_files_needs_a_confirmation()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync(workspace.NewGameWithMods());
        await workspace.CreateProfileAsync(game);
        var wrong = Path.Combine(workspace.GamesRoot, "wrong-mods");
        BackendWorkspace.WriteFile(Path.Combine(wrong, "unrelated.txt"), "something else");

        var refused = await workspace.Games.UpdateAsync(game with { Mods = new ModsLocation(wrong) });

        Assert.Equal("error.gameService.folderMismatch", refused.Error?.MessageId);
        Assert.Equal("mods", refused.Error!.Args["role"]);
        Assert.Equal(0, refused.Error.Args["presentCount"]);
        Assert.Equal(2, refused.Error.Args["recordedCount"]);
        Assert.Equal(game.Mods!.Path, (await workspace.Games.ListAsync(workspace.Token)).Value!.Single().Mods!.Path);
    }

    [Fact]
    public async Task Taking_the_mods_folder_off_a_game_and_putting_it_back_reconnects_the_history_it_kept()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync(workspace.NewGameWithMods());
        var (profile, _) = await workspace.CreateProfileAsync(game);

        var removed = await workspace.Games.UpdateAsync(game with { Mods = null });
        var markerAfterRemoval = ConsentMarker.Read(game.Mods!.Path);
        var restored = await workspace.Games.UpdateAsync(game);

        Assert.True(removed.Ok, removed.Error?.Detail);
        Assert.IsType<ConsentMarkerState.Missing>(markerAfterRemoval);
        Assert.True(restored.Ok, restored.Error?.Detail);
        Assert.Equal(game.Id, Assert.IsType<ConsentMarkerState.Valid>(ConsentMarker.Read(game.Mods.Path)).Marker.GameId);
        var kept = await workspace.Backend.Services.Storage.ReadAsync(async storage =>
            await storage.Engine.ListBranchesAsync(new Savepoint.Core.Git.RepoRef(storage.GitDirectory(game.Id, RepoRole.Mods), game.Mods.Path, game.Id, RepoRole.Mods), workspace.Token), workspace.Token);
        Assert.Contains(profile.BranchName, kept);
    }

    [Fact]
    public async Task History_left_by_a_removed_game_includes_its_mods_folder_and_reconnects_to_it()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync(workspace.NewGameWithMods());
        var (profile, _) = await workspace.CreateProfileAsync(game, "Vanilla");
        File.Delete(Path.Combine(workspace.CatalogDirectory, CatalogStore.GamesFileName));
        File.Delete(Path.Combine(workspace.CatalogDirectory, CatalogStore.ProfilesFileName));
        workspace.Restart();

        var asked = await workspace.Games.AddAsync(game with { Id = "alpha-again" });
        var found = Assert.IsType<AddGameOutcome.HistoryFound>(asked.Value).History;
        var reconnected = await workspace.Games.ResolveOrphanAsync(
            game with { Id = "alpha-again" },
            new OrphanResolution.Reconnect(found.ExistingGameId, [new RecoveredProfile(profile.BranchName, "Vanilla")]));

        Assert.Contains(RepoRole.Mods, found.Roles);
        Assert.True(reconnected.Ok, reconnected.Error?.Detail);
        Assert.Equal(game.Id, reconnected.Value.Game.Id);
        Assert.Equal(game.Mods, reconnected.Value.Game.Mods);
        var history = Assert.IsType<ConsentMarkerState.Valid>(ConsentMarker.Read(game.Mods!.Path));
        Assert.Equal(game.Id, history.Marker.GameId);
    }

    [Fact]
    public void A_marker_that_names_a_role_nobody_wrote_is_still_corrupt()
    {
        using var workspace = new BackendWorkspace();
        var folder = Path.Combine(workspace.GamesRoot, "odd");
        BackendWorkspace.WriteFile(
            Path.Combine(folder, ConsentMarker.FileName),
            "{\"gameId\":\"a\",\"role\":\"Mods\",\"confirmedAt\":\"2026-10-20T14:03:11.402Z\"}");

        Assert.IsType<ConsentMarkerState.Corrupt>(ConsentMarker.Read(folder));
    }
}
