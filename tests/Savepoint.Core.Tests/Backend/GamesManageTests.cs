using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Git;
using Savepoint.Core.Paths;

namespace Savepoint.Core.Tests.Backend;

public class GamesManageTests
{
    [Fact]
    public async Task Deleting_a_game_removes_its_row_profiles_markers_and_storage_but_not_a_single_game_file()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync();
        var (profile, _) = await workspace.CreateProfileAsync(game);
        var installBefore = BackendWorkspace.Snapshot(game.Install.Path);
        var savesBefore = BackendWorkspace.Snapshot(game.External!.Path);

        var deleted = await workspace.Games.DeleteAsync(game.Id);

        Assert.True(deleted.Ok, deleted.Error?.Detail);
        Assert.Empty((await workspace.Games.ListAsync(workspace.Token)).Value!);
        Assert.DoesNotContain(profile.Id, (await workspace.Profiles.ListAsync(null, workspace.Token)).Value!.Select(p => p.Id));
        Assert.False(Directory.Exists(Path.Combine(workspace.DefaultStorageRoot, game.Id)));
        Assert.IsType<ConsentMarkerState.Missing>(ConsentMarker.Read(game.Install.Path));
        Assert.IsType<ConsentMarkerState.Missing>(ConsentMarker.Read(game.External.Path));
        Assert.Equal(installBefore.Where(f => f.Key != ConsentMarker.FileName), BackendWorkspace.Snapshot(game.Install.Path));
        Assert.Equal(savesBefore.Where(f => f.Key != ConsentMarker.FileName), BackendWorkspace.Snapshot(game.External.Path));
    }

    [Fact]
    public async Task The_same_folders_can_be_added_again_as_a_new_game_after_a_delete()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync();
        await workspace.Games.DeleteAsync(game.Id);

        var added = await workspace.Games.AddAsync(game with { Id = "alpha-reborn" });

        Assert.IsType<AddGameOutcome.Added>(added.Value);
    }

    [Fact]
    public async Task A_running_game_cannot_be_deleted_and_nothing_is_removed()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync();
        await workspace.CreateProfileAsync(game);
        workspace.Sessions.Start(game.Id, "any");

        var deleted = await workspace.Games.DeleteAsync(game.Id);

        Assert.Equal("error.gameService.gameRunning", deleted.Error?.MessageId);
        Assert.Equal(game.Name, deleted.Error!.Args["gameName"]);
        Assert.Single((await workspace.Games.ListAsync(workspace.Token)).Value!);
        Assert.Single((await workspace.Profiles.ListAsync(game.Id, workspace.Token)).Value!);
        Assert.True(Directory.Exists(Path.Combine(workspace.DefaultStorageRoot, game.Id, "install.git")));
        Assert.IsType<ConsentMarkerState.Valid>(ConsentMarker.Read(game.Install.Path));
    }

    [Fact]
    public async Task Another_games_running_session_does_not_block_a_delete()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync(workspace.NewGame("Alpha"));
        workspace.Sessions.Start("beta-9", "any");

        Assert.True((await workspace.Games.DeleteAsync(game.Id)).Ok);
    }

    [Fact]
    public async Task Deleting_a_game_whose_folders_have_moved_still_removes_its_storage()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync();
        Directory.Move(game.Install.Path, game.Install.Path + "-moved");
        Directory.Move(game.External!.Path, game.External.Path + "-moved");

        var deleted = await workspace.Games.DeleteAsync(game.Id);

        Assert.True(deleted.Ok, deleted.Error?.Detail);
        Assert.False(Directory.Exists(Path.Combine(workspace.DefaultStorageRoot, game.Id)));
        Assert.Empty((await workspace.Games.ListAsync(workspace.Token)).Value!);
    }

    [Fact]
    public async Task Deleting_an_unknown_game_is_an_error()
    {
        using var workspace = new BackendWorkspace();

        Assert.False((await workspace.Games.DeleteAsync("nope")).Ok);
    }

    [Fact]
    public async Task A_rename_keeps_the_history_and_the_markers()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync();
        await workspace.CreateProfileAsync(game);

        var updated = await workspace.Games.UpdateAsync(game with { Name = "Alpha Renamed", Notes = "my notes" });

        Assert.True(updated.Ok, updated.Error?.Detail);
        Assert.Equal("Alpha Renamed", updated.Value.Name);
        Assert.Equal("Alpha Renamed", (await workspace.Games.ListAsync(workspace.Token)).Value!.Single().Name);
        Assert.Single((await workspace.History.GetAsync(game.Id, null, workspace.Token)).Value!.Points);
        Assert.IsType<ConsentMarkerState.Valid>(ConsentMarker.Read(game.Install.Path));
    }

    [Fact]
    public async Task Ignore_rules_changed_in_an_edit_apply_to_the_next_capture()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync();
        var (profile, _) = await workspace.CreateProfileAsync(game);
        BackendWorkspace.WriteFile(Path.Combine(game.Install.Path, "cache", "tmp.bin"), "cache");
        await workspace.Games.UpdateAsync(game with { IgnorePaths = ["cache/"] });

        var started = await workspace.Profiles.CaptureAsync(game.Id, profile.Id, "After the rule", [RepoRole.Install]);
        await workspace.WaitForJobAsync(started.Value!.JobId);

        var storage = await workspace.Backend.Services.Storage.GetAsync(workspace.Token);
        var install = new RepoRef(storage.GitDirectory(game.Id, RepoRole.Install), game.Install.Path, game.Id, RepoRole.Install);
        Assert.DoesNotContain("cache/tmp.bin", await storage.Engine.ListTreeFilesAsync(install, profile.BranchName, workspace.Token));
    }

    [Fact]
    public async Task A_folder_that_was_moved_can_be_pointed_at_its_new_place_and_keeps_its_history()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync();
        var (profile, _) = await workspace.CreateProfileAsync(game);
        var moved = game.Install.Path + "-moved";
        Directory.Move(game.Install.Path, moved);

        var updated = await workspace.Games.UpdateAsync(game with { Install = new InstallLocation(moved) });

        Assert.True(updated.Ok, updated.Error?.Detail);
        var folders = (await workspace.Games.GetFolderStatusAsync(game.Id, workspace.Token)).Value!;
        Assert.Equal(moved, folders[0].ResolvedPath);
        Assert.True(folders[0].Exists);
        Assert.True(folders[0].Consent.Confirmed);
        var row = (await workspace.Profiles.GetRowStatsAsync(game.Id, workspace.Token)).Value!.Single(r => r.ProfileId == profile.Id);
        Assert.True(row.IsActive);
    }

    [Fact]
    public async Task A_folder_pointed_at_a_different_place_is_claimed_there_and_released_at_the_old_place()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync();
        var elsewhere = Path.Combine(workspace.GamesRoot, "elsewhere");
        Directory.CreateDirectory(elsewhere);

        var updated = await workspace.Games.UpdateAsync(game with { Install = new InstallLocation(elsewhere) });

        Assert.True(updated.Ok, updated.Error?.Detail);
        var claimed = Assert.IsType<ConsentMarkerState.Valid>(ConsentMarker.Read(elsewhere));
        Assert.Equal(game.Id, claimed.Marker.GameId);
        Assert.IsType<ConsentMarkerState.Missing>(ConsentMarker.Read(game.Install.Path));
    }

    [Fact]
    public async Task Adding_a_saves_folder_to_a_game_that_had_none_makes_its_repository()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync(workspace.NewGame(withSaves: false));
        var saves = Path.Combine(workspace.GamesRoot, "new-saves");
        Directory.CreateDirectory(saves);

        var updated = await workspace.Games.UpdateAsync(game with { External = new ExternalLocation(saves) });

        Assert.True(updated.Ok, updated.Error?.Detail);
        Assert.True(Directory.Exists(Path.Combine(workspace.DefaultStorageRoot, game.Id, "external.git")));
        Assert.IsType<ConsentMarkerState.Valid>(ConsentMarker.Read(saves));
    }

    [Fact]
    public async Task An_edit_that_points_a_folder_somewhere_protected_is_refused_and_changes_nothing()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync();

        var updated = await workspace.Games.UpdateAsync(game with { External = new ExternalLocation(workspace.Home) });

        Assert.Equal("error.pathSafety.tier2Blocked", updated.Error?.MessageId);
        Assert.Equal("external", updated.Error!.Args["role"]);
        Assert.Equal(game.External!.Path, (await workspace.Games.ListAsync(workspace.Token)).Value!.Single().External!.Path);
    }

    [Fact]
    public async Task An_edit_that_overlaps_another_game_is_refused()
    {
        using var workspace = new BackendWorkspace();
        var alpha = await workspace.AddGameAsync(workspace.NewGame("Alpha"));
        var beta = await workspace.AddGameAsync(workspace.NewGame("Beta"));

        var updated = await workspace.Games.UpdateAsync(beta with { Install = new InstallLocation(Path.Combine(alpha.Install.Path, "data")) });

        Assert.Equal("error.gameService.pathOverlap", updated.Error?.MessageId);
        Assert.Equal(alpha.Id, updated.Error!.Args["conflictingGameId"]);
        Assert.Equal("install", updated.Error.Args["role"]);
    }

    [Fact]
    public async Task Editing_an_unknown_game_is_an_error()
    {
        using var workspace = new BackendWorkspace();

        Assert.False((await workspace.Games.UpdateAsync(workspace.NewGame())).Ok);
    }

    [Fact]
    public async Task The_game_keeps_the_id_it_was_added_under_whatever_the_edit_says()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync();

        var updated = await workspace.Games.UpdateAsync(game with { Name = "Alpha 2" });

        Assert.Equal(game.Id, updated.Value!.Id);
    }
}
