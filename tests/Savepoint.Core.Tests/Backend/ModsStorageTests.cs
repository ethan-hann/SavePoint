using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Tests.Git;

namespace Savepoint.Core.Tests.Backend;

// The mods store is one of a game's three git directories wherever the storage code lists, finds, moves, or clears them.
public class ModsStorageTests
{
    [Fact]
    public async Task The_snapshot_names_the_mods_git_directory_under_the_games_own_folder()
    {
        using var workspace = new BackendWorkspace();

        var snapshot = await workspace.Backend.Services.Storage.GetAsync(workspace.Token);

        Assert.Equal(Path.Combine(workspace.DefaultStorageRoot, "alpha-1", "mods.git"), snapshot.GitDirectory("alpha-1", RepoRole.Mods));
    }

    [Fact(Timeout = 120_000)]
    public async Task Find_the_folder_recognizes_a_game_by_its_mods_store_alone()
    {
        using var workspace = new BackendWorkspace();
        await workspace.AppStatus.CheckAsync(workspace.Token);
        var game = await workspace.AddGameAsync(workspace.NewGameWithMods());
        await workspace.CreateProfileAsync(game);
        await workspace.WaitForIdleAsync(TimeSpan.FromSeconds(60));
        var away = workspace.DefaultStorageRoot + "-away";
        BackendWorkspace.MoveFolder(workspace.DefaultStorageRoot, away);
        GitWorkspace.DeleteDirectory(Path.Combine(away, game.Id, "install.git"));
        GitWorkspace.DeleteDirectory(Path.Combine(away, game.Id, "external.git"));
        workspace.Restart();

        var found = await workspace.AppStatus.UseStorageFolderAsync(away);

        Assert.True(found.Ok, found.Error?.Detail);
        Assert.Equal(away, (await workspace.Settings.GetAsync(workspace.Token)).Value!.StorageRoot);
    }

    [Fact(Timeout = 120_000)]
    public async Task Find_the_folder_makes_an_adopted_mods_store_safe_before_anything_reads_it()
    {
        using var workspace = new BackendWorkspace();
        await workspace.AppStatus.CheckAsync(workspace.Token);
        var game = await workspace.AddGameAsync(workspace.NewGameWithMods());
        var (profile, _) = await workspace.CreateProfileAsync(game);
        await workspace.WaitForIdleAsync(TimeSpan.FromSeconds(60));
        var away = workspace.DefaultStorageRoot + "-away";
        BackendWorkspace.MoveFolder(workspace.DefaultStorageRoot, away);
        var gitDir = Path.Combine(away, game.Id, "mods.git");
        var hookRan = Path.Combine(workspace.Root, "hook-ran.txt");
        BackendWorkspace.WriteFile(Path.Combine(gitDir, "hooks", "post-commit"), $"#!/bin/sh\necho ran >> '{hookRan.Replace('\\', '/')}'\n");
        File.AppendAllText(Path.Combine(gitDir, "config"), "[alias]\n\tst = !echo ran\n");
        workspace.Restart();

        var found = await workspace.AppStatus.UseStorageFolderAsync(away);

        Assert.True(found.Ok, found.Error?.Detail);
        Assert.DoesNotContain("[alias]", File.ReadAllText(Path.Combine(gitDir, "config")), StringComparison.OrdinalIgnoreCase);
        Assert.Empty(Directory.GetFileSystemEntries(Path.Combine(gitDir, "hooks")));
        Assert.Equal(profile.BranchName, Assert.Single((await workspace.Profiles.ListAsync(game.Id, workspace.Token)).Value!).BranchName);
    }

    [Fact(Timeout = 120_000)]
    public async Task Moving_the_storage_carries_the_mods_store_and_removes_the_old_one()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync(workspace.NewGameWithMods());
        await workspace.CreateProfileAsync(game);
        await workspace.WaitForIdleAsync(TimeSpan.FromSeconds(60));
        var destination = Path.Combine(workspace.Root, "new-storage");

        var started = await workspace.Settings.RelocateStorageAsync(destination);
        var job = await workspace.WaitForJobAsync(started.Value!.JobId, TimeSpan.FromSeconds(120));

        Assert.Equal(JobStatus.Succeeded, job.Status);
        Assert.Empty(Assert.IsType<StorageRelocationResult>(job.Result).NotRemoved);
        Assert.True(Directory.Exists(Path.Combine(destination, game.Id, "mods.git")));
        Assert.False(Directory.Exists(Path.Combine(workspace.DefaultStorageRoot, game.Id)), "no store may stay behind on the old drive");
        Assert.Single((await workspace.History.GetAsync(game.Id, null, workspace.Token)).Value!.Points);
    }

    [Fact(Timeout = 120_000)]
    public async Task Clearing_all_data_removes_the_mods_store_of_a_game_the_catalog_no_longer_lists()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync(workspace.NewGameWithMods());
        await workspace.WaitForIdleAsync(TimeSpan.FromSeconds(60));
        File.Delete(Path.Combine(workspace.CatalogDirectory, CatalogStore.GamesFileName));
        workspace.Restart();

        var started = await workspace.Settings.ClearAllDataAsync();
        var job = await workspace.WaitForJobAsync(started.Value!.JobId, TimeSpan.FromSeconds(120));

        Assert.Equal(JobStatus.Succeeded, job.Status);
        Assert.False(Directory.Exists(workspace.StoreOf(game, "mods.git")));
        Assert.False(Directory.Exists(workspace.StoreOf(game, "install.git")));
    }
}
