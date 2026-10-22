using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Paths;
using Savepoint.Core.Services.Games;

namespace Savepoint.Core.Tests.Backend;

public class OrphanTests
{
    [Fact]
    public async Task History_left_by_a_game_that_is_gone_is_offered_back_instead_of_being_added_over()
    {
        using var workspace = new BackendWorkspace();
        var (game, profile) = await LoseTheCatalogAsync(workspace);

        var added = await workspace.Games.AddAsync(game with { Id = "alpha-again" });

        Assert.True(added.Ok, added.Error?.Detail);
        var found = Assert.IsType<AddGameOutcome.HistoryFound>(added.Value).History;
        Assert.Equal(game.Id, found.ExistingGameId);
        Assert.Equal([RepoRole.Install, RepoRole.External], found.Roles);
        var recoverable = Assert.Single(found.Profiles);
        Assert.Equal(profile.BranchName, recoverable.BranchName);
        Assert.Equal("Vanilla", recoverable.SuggestedName);
        Assert.Empty((await workspace.Games.ListAsync(workspace.Token)).Value!);
    }

    [Fact]
    public async Task Asking_changes_nothing_on_disk()
    {
        using var workspace = new BackendWorkspace();
        var (game, _) = await LoseTheCatalogAsync(workspace);

        await workspace.Games.AddAsync(game with { Id = "alpha-again" });

        var marker = Assert.IsType<ConsentMarkerState.Valid>(ConsentMarker.Read(game.Install.Path));
        Assert.Equal(game.Id, marker.Marker.GameId);
        Assert.True(Directory.Exists(Path.Combine(workspace.DefaultStorageRoot, game.Id, "install.git")));
    }

    [Fact]
    public async Task Reconnecting_adds_the_game_under_its_old_id_and_brings_the_profiles_back_with_their_history()
    {
        using var workspace = new BackendWorkspace();
        var (game, profile) = await LoseTheCatalogAsync(workspace);

        var resolved = await workspace.Games.ResolveOrphanAsync(
            game with { Id = "alpha-again" },
            new OrphanResolution.Reconnect(game.Id, [new RecoveredProfile(profile.BranchName, "Vanilla (recovered)")]));

        Assert.True(resolved.Ok, resolved.Error?.Detail);
        Assert.Equal(game.Id, resolved.Value.Id);
        Assert.Equal([game.Id], (await workspace.Games.ListAsync(workspace.Token)).Value!.Select(g => g.Id));
        var back = Assert.Single((await workspace.Profiles.ListAsync(game.Id, workspace.Token)).Value!);
        Assert.Equal(profile.Id, back.Id);
        Assert.Equal(profile.BranchName, back.BranchName);
        Assert.Equal("Vanilla (recovered)", back.Name);
        var point = Assert.Single((await workspace.History.GetAsync(game.Id, null, workspace.Token)).Value!.Points);
        Assert.Equal("Vanilla", point.Name);
        var row = Assert.Single((await workspace.Profiles.GetRowStatsAsync(game.Id, workspace.Token)).Value!);
        Assert.True(row.IsActive);
    }

    [Fact]
    public async Task Starting_fresh_adds_the_game_under_the_new_id_and_leaves_the_old_history_on_disk()
    {
        using var workspace = new BackendWorkspace();
        var (game, _) = await LoseTheCatalogAsync(workspace);

        var resolved = await workspace.Games.ResolveOrphanAsync(game with { Id = "alpha-again" }, new OrphanResolution.StartFresh());

        Assert.True(resolved.Ok, resolved.Error?.Detail);
        Assert.Equal("alpha-again", resolved.Value.Id);
        var marker = Assert.IsType<ConsentMarkerState.Valid>(ConsentMarker.Read(game.Install.Path));
        Assert.Equal("alpha-again", marker.Marker.GameId);
        Assert.Empty((await workspace.Profiles.ListAsync(null, workspace.Token)).Value!);
        Assert.True(Directory.Exists(Path.Combine(workspace.DefaultStorageRoot, game.Id, "install.git")));
    }

    [Fact]
    public async Task A_marker_with_no_history_behind_it_is_let_go_without_a_question()
    {
        using var workspace = new BackendWorkspace();
        var game = workspace.NewGame();
        ConsentMarker.Write(game.Install.Path, "long-gone", RepoRole.Install, DateTimeOffset.UtcNow);
        ConsentMarker.Write(game.External!.Path, "long-gone", RepoRole.External, DateTimeOffset.UtcNow);

        var added = await workspace.Games.AddAsync(game);

        Assert.IsType<AddGameOutcome.Added>(added.Value);
        Assert.Equal(game.Id, Assert.IsType<ConsentMarkerState.Valid>(ConsentMarker.Read(game.Install.Path)).Marker.GameId);
    }

    [Fact]
    public async Task Reconnecting_to_an_id_that_is_not_the_one_found_is_refused()
    {
        using var workspace = new BackendWorkspace();
        var (game, profile) = await LoseTheCatalogAsync(workspace);

        var resolved = await workspace.Games.ResolveOrphanAsync(
            game with { Id = "alpha-again" },
            new OrphanResolution.Reconnect("someone-else", [new RecoveredProfile(profile.BranchName, "Vanilla")]));

        Assert.Equal("error.gameService.noHistoryToReconnect", resolved.Error?.MessageId);
        Assert.Equal("someone-else", resolved.Error!.Args["gameId"]);
        Assert.Empty((await workspace.Games.ListAsync(workspace.Token)).Value!);
    }

    [Fact]
    public async Task A_branch_the_old_game_never_had_is_not_turned_into_a_profile()
    {
        using var workspace = new BackendWorkspace();
        var (game, profile) = await LoseTheCatalogAsync(workspace);

        var resolved = await workspace.Games.ResolveOrphanAsync(
            game with { Id = "alpha-again" },
            new OrphanResolution.Reconnect(game.Id, [new RecoveredProfile(profile.BranchName, "Vanilla"), new RecoveredProfile("profile/invented-zzzz", "Made up")]));

        Assert.True(resolved.Ok, resolved.Error?.Detail);
        Assert.Equal([profile.Id], (await workspace.Profiles.ListAsync(game.Id, workspace.Token)).Value!.Select(p => p.Id));
    }

    [Theory]
    [InlineData("profile/modded-playthrough-x7k2", "Modded playthrough")]
    [InlineData("profile/vanilla-ab12", "Vanilla")]
    [InlineData("profile/profile-ab12", null)]
    [InlineData("profile/game-ab12", null)]
    [InlineData("profile/", null)]
    [InlineData("profile/my_old-save-0000", "My old save")]
    [InlineData("custom/branch", "Custom branch")]
    public void A_suggested_name_is_read_back_from_the_branch_name(string branch, string? expected)
    {
        Assert.Equal(expected, OrphanScanner.SuggestName(branch));
    }

    // A game with a profile and history, then the catalog files are lost: only the git directories and markers remain.
    private static async Task<(GameDefinition Game, Profile Profile)> LoseTheCatalogAsync(BackendWorkspace workspace)
    {
        var game = await workspace.AddGameAsync();
        var (profile, _) = await workspace.CreateProfileAsync(game, "Vanilla");
        File.Delete(Path.Combine(workspace.CatalogDirectory, CatalogStore.GamesFileName));
        File.Delete(Path.Combine(workspace.CatalogDirectory, CatalogStore.ProfilesFileName));
        workspace.Restart();
        return (game, profile);
    }
}
