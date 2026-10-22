using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;
using Savepoint.Core.Paths;

namespace Savepoint.Core.Tests.Backend;

public class GamesAddTests
{
    [Fact]
    public async Task Adding_a_game_whose_id_is_taken_is_refused_with_its_own_message()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync();

        var again = await workspace.Games.AddAsync(game);
        var resolved = await workspace.Games.ResolveOrphanAsync(game, new OrphanResolution.StartFresh());

        Assert.Equal("error.gameService.alreadyExists", again.Error?.MessageId);
        Assert.Equal(game.Id, again.Error!.Args["gameId"]);
        Assert.Equal("error.gameService.alreadyExists", resolved.Error?.MessageId);
    }

    [Fact]
    public async Task A_new_game_is_listed_with_both_folders_confirmed_and_tracked()
    {
        using var workspace = new BackendWorkspace();
        var definition = workspace.NewGame();

        var added = await workspace.Games.AddAsync(definition);

        Assert.True(added.Ok, added.Error?.Detail);
        var outcome = Assert.IsType<AddGameOutcome.Added>(added.Value);
        Assert.Equal(definition.Id, outcome.Game.Id);
        var listed = await workspace.Games.ListAsync(workspace.Token);
        Assert.Equal([definition.Id], listed.Value!.Select(g => g.Id));
        AssertMarker(definition.Install.Path, definition.Id, RepoRole.Install);
        AssertMarker(definition.External!.Path, definition.Id, RepoRole.External);
        Assert.True(Directory.Exists(Path.Combine(workspace.DefaultStorageRoot, definition.Id, "install.git")));
        Assert.True(Directory.Exists(Path.Combine(workspace.DefaultStorageRoot, definition.Id, "external.git")));
    }

    [Fact]
    public async Task The_saved_definition_is_the_normalized_one()
    {
        using var workspace = new BackendWorkspace();
        var definition = workspace.NewGame(ignore: ["  cache/  ", "cache/", ""]);

        var game = await workspace.AddGameAsync(definition with { Name = "  Alpha  " });

        Assert.Equal("Alpha", game.Name);
        Assert.Equal(["cache/"], game.IgnorePaths);
    }

    [Fact]
    public async Task A_game_without_a_saves_folder_gets_only_an_install_repository()
    {
        using var workspace = new BackendWorkspace();
        var definition = workspace.NewGame(withSaves: false);

        await workspace.AddGameAsync(definition);

        Assert.True(Directory.Exists(Path.Combine(workspace.DefaultStorageRoot, definition.Id, "install.git")));
        Assert.False(Directory.Exists(Path.Combine(workspace.DefaultStorageRoot, definition.Id, "external.git")));
    }

    [Fact]
    public async Task Games_survive_a_backend_restart()
    {
        using var workspace = new BackendWorkspace();
        var definition = workspace.NewGame();
        await workspace.AddGameAsync(definition);

        var restarted = workspace.Restart();

        var listed = await restarted.Games.ListAsync(workspace.Token);
        Assert.Equal([definition.Id], listed.Value!.Select(g => g.Id));
        var folders = await restarted.Games.GetFolderStatusAsync(definition.Id, workspace.Token);
        Assert.All(folders.Value!, folder => Assert.True(folder.Consent.Confirmed));
    }

    [Fact]
    public async Task A_blank_name_is_refused_with_the_field_that_is_wrong_and_nothing_is_written()
    {
        using var workspace = new BackendWorkspace();
        var definition = workspace.NewGame() with { Name = "  " };

        var added = await workspace.Games.AddAsync(definition);

        Assert.Equal("error.validation.invalidPayload", added.Error?.MessageId);
        var issues = Assert.IsAssignableFrom<IEnumerable<ValidationIssue>>(added.Error!.Args["issues"]);
        Assert.Contains(issues, issue => issue.Path == "name");
        AssertNothingWritten(workspace, definition);
    }

    [Fact]
    public async Task An_unknown_path_token_is_reported_on_the_install_field()
    {
        using var workspace = new BackendWorkspace();
        var definition = workspace.NewGame() with { Install = new InstallLocation("{nowhere}/Games/Alpha") };

        var added = await workspace.Games.AddAsync(definition);

        Assert.Equal("error.validation.invalidPayload", added.Error?.MessageId);
        var issue = Assert.Single(Assert.IsAssignableFrom<IEnumerable<ValidationIssue>>(added.Error!.Args["issues"]));
        Assert.Equal("install.path", issue.Path);
        Assert.Equal(PathValidationIds.UnknownToken, issue.MessageId);
    }

    [Fact]
    public async Task A_relative_saves_path_is_reported_on_the_saves_field()
    {
        using var workspace = new BackendWorkspace();
        var definition = workspace.NewGame() with { External = new ExternalLocation("saves/here") };

        var added = await workspace.Games.AddAsync(definition);

        var issue = Assert.Single(Assert.IsAssignableFrom<IEnumerable<ValidationIssue>>(added.Error!.Args["issues"]));
        Assert.Equal("external.path", issue.Path);
        Assert.Equal(PathValidationIds.NotAbsolute, issue.MessageId);
    }

    [Fact]
    public async Task A_protected_install_folder_is_refused_and_names_the_install_role()
    {
        using var workspace = new BackendWorkspace();
        var definition = workspace.NewGame() with { Install = new InstallLocation(workspace.Home) };

        var added = await workspace.Games.AddAsync(definition);

        Assert.Equal("error.pathSafety.tier2Blocked", added.Error?.MessageId);
        Assert.Equal("install", added.Error!.Args["role"]);
        Assert.Equal(workspace.Home, added.Error.Args["path"]);
        AssertNothingWritten(workspace, definition);
    }

    [Fact]
    public async Task A_folder_inside_savepoints_own_storage_is_refused_and_names_the_saves_role()
    {
        using var workspace = new BackendWorkspace();
        var inside = Path.Combine(workspace.DefaultStorageRoot, "saves-in-storage");
        Directory.CreateDirectory(inside);
        var definition = workspace.NewGame() with { External = new ExternalLocation(inside) };

        var added = await workspace.Games.AddAsync(definition);

        Assert.Equal("error.pathSafety.tier1Blocked", added.Error?.MessageId);
        Assert.Equal("external", added.Error!.Args["role"]);
    }

    [Fact]
    public async Task A_folder_inside_the_apps_own_install_folder_is_refused()
    {
        using var workspace = new BackendWorkspace();
        var inside = Path.Combine(workspace.InstallDirectory, "plugins");
        Directory.CreateDirectory(inside);
        var definition = workspace.NewGame() with { Install = new InstallLocation(inside) };

        var added = await workspace.Games.AddAsync(definition);

        Assert.Equal("error.pathSafety.tier1Blocked", added.Error?.MessageId);
        Assert.Equal("install", added.Error!.Args["role"]);
    }

    [Fact]
    public async Task A_folder_that_overlaps_another_game_is_refused_with_that_games_name()
    {
        using var workspace = new BackendWorkspace();
        var first = await workspace.AddGameAsync(workspace.NewGame("Alpha"));
        var nested = Path.Combine(first.Install.Path, "data");
        var second = workspace.NewGame("Beta") with { Install = new InstallLocation(nested) };

        var added = await workspace.Games.AddAsync(second);

        Assert.Equal("error.gameService.pathOverlap", added.Error?.MessageId);
        Assert.Equal(first.Id, added.Error!.Args["conflictingGameId"]);
        Assert.Equal("Alpha", added.Error.Args["conflictingGameName"]);
        Assert.Equal("install", added.Error.Args["role"]);
        Assert.Equal([first.Id], (await workspace.Games.ListAsync(workspace.Token)).Value!.Select(g => g.Id));
        AssertNoMarker(nested);
    }

    [Fact]
    public async Task A_saves_folder_that_overlaps_another_games_install_names_the_saves_role()
    {
        using var workspace = new BackendWorkspace();
        var first = await workspace.AddGameAsync(workspace.NewGame("Alpha"));
        var second = workspace.NewGame("Beta") with { External = new ExternalLocation(first.Install.Path) };

        var added = await workspace.Games.AddAsync(second);

        Assert.Equal("error.gameService.pathOverlap", added.Error?.MessageId);
        Assert.Equal("external", added.Error!.Args["role"]);
    }

    [Fact]
    public async Task A_folder_a_live_game_has_claimed_is_refused_as_claimed()
    {
        using var workspace = new BackendWorkspace();
        var first = await workspace.AddGameAsync(workspace.NewGame("Alpha"));
        var elsewhere = workspace.NewGame("Beta");
        ConsentMarker.Write(elsewhere.Install.Path, first.Id, RepoRole.Install, DateTimeOffset.UtcNow);

        var added = await workspace.Games.AddAsync(elsewhere);

        Assert.Equal("error.consentMarker.conflict", added.Error?.MessageId);
        Assert.Equal("install", added.Error!.Args["role"]);
        Assert.Equal(first.Id, added.Error.Args["existingGameId"]);
    }

    [Fact]
    public async Task A_folder_that_does_not_exist_is_reported_missing_and_the_first_repository_is_not_left_behind()
    {
        using var workspace = new BackendWorkspace();
        var definition = workspace.NewGame();
        Directory.Delete(definition.External!.Path, recursive: true);

        var added = await workspace.Games.AddAsync(definition);

        Assert.Equal("error.path.missing", added.Error?.MessageId);
        Assert.Equal("external", added.Error!.Args["role"]);
        AssertNothingWritten(workspace, definition);
    }

    [Fact]
    public async Task A_second_game_with_the_same_id_is_refused()
    {
        using var workspace = new BackendWorkspace();
        var first = await workspace.AddGameAsync(workspace.NewGame());
        var again = workspace.NewGame("Gamma") with { Id = first.Id };

        var added = await workspace.Games.AddAsync(again);

        Assert.False(added.Ok);
        Assert.Equal([first.Id], (await workspace.Games.ListAsync(workspace.Token)).Value!.Select(g => g.Id));
    }

    [Fact]
    public async Task Resolving_a_path_expands_its_token()
    {
        using var workspace = new BackendWorkspace();

        var resolved = await workspace.Games.ResolvePathAsync("{userHome}/Games/Alpha", workspace.Token);

        Assert.Equal(Path.Combine(workspace.Home, "Games", "Alpha"), resolved.Value);
    }

    [Fact]
    public async Task Resolving_a_path_with_an_unknown_token_answers_a_validation_issue()
    {
        using var workspace = new BackendWorkspace();

        var resolved = await workspace.Games.ResolvePathAsync("{nowhere}/Alpha", workspace.Token);

        Assert.Equal("error.validation.invalidPayload", resolved.Error?.MessageId);
    }

    [Fact]
    public async Task A_cancelled_read_answers_the_cancelled_message()
    {
        using var workspace = new BackendWorkspace();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        var listed = await workspace.Games.ListAsync(cancelled.Token);
        var resolved = await workspace.Games.ResolvePathAsync("{userHome}", cancelled.Token);

        Assert.Equal("error.operation.cancelled", listed.Error?.MessageId);
        Assert.Equal("error.operation.cancelled", resolved.Error?.MessageId);
    }

    [Fact]
    public void The_templates_are_the_bundled_ones()
    {
        using var workspace = new BackendWorkspace();

        Assert.Equal(GameTemplates.All.Select(t => t.Id), workspace.Games.Templates.Select(t => t.Id));
    }

    private static void AssertMarker(string folder, string gameId, RepoRole role)
    {
        var marker = Assert.IsType<ConsentMarkerState.Valid>(ConsentMarker.Read(folder));
        Assert.Equal(gameId, marker.Marker.GameId);
        Assert.Equal(role, marker.Marker.Role);
    }

    private static void AssertNoMarker(string folder) =>
        Assert.IsType<ConsentMarkerState.Missing>(ConsentMarker.Read(folder));

    // A refused add changes nothing: no row, no marker in either folder, no git directory.
    private static void AssertNothingWritten(BackendWorkspace workspace, GameDefinition definition)
    {
        Assert.False(File.Exists(Path.Combine(workspace.CatalogDirectory, CatalogStore.GamesFileName)));
        foreach (var folder in new[] { definition.Install.Path, definition.External?.Path }.OfType<string>())
        {
            if (Directory.Exists(folder))
            {
                AssertNoMarker(folder);
            }
        }

        Assert.False(Directory.Exists(Path.Combine(workspace.DefaultStorageRoot, definition.Id, "install.git")));
        Assert.False(Directory.Exists(Path.Combine(workspace.DefaultStorageRoot, definition.Id, "external.git")));
    }
}
