using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Paths;

namespace Savepoint.Core.Tests.Backend;

public class ClearAllDataTests
{
    [Fact]
    public async Task The_preview_counts_what_will_go_and_names_the_folders_that_lose_a_marker()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync();
        await workspace.CreateProfileAsync(game);

        var preview = (await workspace.Settings.GetClearAllDataPreviewAsync(workspace.Token)).Value!;

        Assert.Equal(workspace.DefaultStorageRoot, preview.StorageRoot);
        Assert.True(preview.StorageBytes > 0);
        Assert.Equal(1, preview.GameCount);
        Assert.Equal(1, preview.ProfileCount);
        Assert.Equal(workspace.CatalogDirectory, preview.CatalogDirectory);
        Assert.Equal([game.Install.Path, game.External!.Path], preview.ConsentMarkerFolders);
        Assert.Null(preview.BlockedBy);
    }

    [Fact]
    public async Task The_preview_of_an_empty_library_is_empty()
    {
        using var workspace = new BackendWorkspace();

        var preview = (await workspace.Settings.GetClearAllDataPreviewAsync(workspace.Token)).Value!;

        Assert.Equal(0, preview.GameCount);
        Assert.Equal(0, preview.ProfileCount);
        Assert.Empty(preview.ConsentMarkerFolders);
        Assert.Null(preview.BlockedBy);
    }

    [Fact]
    public async Task A_running_game_blocks_the_clear_and_nothing_is_removed()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync();
        workspace.Sessions.Start(game.Id, "any");

        var preview = (await workspace.Settings.GetClearAllDataPreviewAsync(workspace.Token)).Value!;
        var started = await workspace.Settings.ClearAllDataAsync();

        Assert.Equal("error.gameService.gameRunning", preview.BlockedBy?.MessageId);
        Assert.Equal("error.gameService.gameRunning", started.Error?.MessageId);
        Assert.Empty(workspace.Jobs.List());
        Assert.Single((await workspace.Games.ListAsync(workspace.Token)).Value!);
        Assert.True(Directory.Exists(Path.Combine(workspace.DefaultStorageRoot, game.Id, "install.git")));
    }

    [Fact]
    public async Task Clearing_leaves_the_app_at_first_run_and_every_game_file_exactly_as_it_was()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync();
        var (profile, _) = await workspace.CreateProfileAsync(game);
        await workspace.WaitForIdleAsync();

        // Edits made after the last save are exactly what must survive: clearing is not a rollback.
        File.WriteAllText(Path.Combine(game.Install.Path, "data", "base.pak"), "edited since the last save");
        BackendWorkspace.WriteFile(Path.Combine(game.External!.Path, "autosave.sav"), "unsaved progress");
        var installBefore = BackendWorkspace.Snapshot(game.Install.Path).Where(f => f.Key != ConsentMarker.FileName).ToDictionary();
        var savesBefore = BackendWorkspace.Snapshot(game.External.Path).Where(f => f.Key != ConsentMarker.FileName).ToDictionary();
        await workspace.Settings.SaveAsync(AppSettings.Defaults(workspace.DefaultStorageRoot) with { Theme = ThemePreference.Dark });

        var started = await workspace.Settings.ClearAllDataAsync();
        var job = await workspace.WaitForJobAsync(started.Value!.JobId);

        Assert.Equal(JobStatus.Succeeded, job.Status);
        Assert.Equal(JobKind.ClearData, job.Kind);
        Assert.False(job.Cancellable);
        var result = Assert.IsType<ClearAllDataResult>(job.Result);
        Assert.Empty(result.NotRemoved);
        Assert.True(result.BytesFreed > 0);
        Assert.Equal(installBefore, BackendWorkspace.Snapshot(game.Install.Path));
        Assert.Equal(savesBefore, BackendWorkspace.Snapshot(game.External.Path));
        Assert.IsType<ConsentMarkerState.Missing>(ConsentMarker.Read(game.Install.Path));
        Assert.IsType<ConsentMarkerState.Missing>(ConsentMarker.Read(game.External.Path));
        Assert.Empty((await workspace.Games.ListAsync(workspace.Token)).Value!);
        Assert.Empty((await workspace.Profiles.ListAsync(null, workspace.Token)).Value!);
        Assert.Equal(AppSettings.Defaults(workspace.DefaultStorageRoot), (await workspace.Settings.GetAsync(workspace.Token)).Value);
        Assert.False(Directory.Exists(Path.Combine(workspace.DefaultStorageRoot, game.Id)));
        foreach (var file in new[] { CatalogStore.GamesFileName, CatalogStore.ProfilesFileName, CatalogStore.SettingsFileName })
        {
            Assert.False(File.Exists(Path.Combine(workspace.CatalogDirectory, file)), file);
            Assert.False(File.Exists(Path.Combine(workspace.CatalogDirectory, file + ".bak")), file + ".bak");
        }

        Assert.NotNull(profile);
    }

    [Fact]
    public async Task After_clearing_the_same_folders_can_be_added_as_a_brand_new_game()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync();
        await workspace.CreateProfileAsync(game);
        var started = await workspace.Settings.ClearAllDataAsync();
        await workspace.WaitForJobAsync(started.Value!.JobId);

        var added = await workspace.Games.AddAsync(game);

        Assert.IsType<AddGameOutcome.Added>(added.Value);
    }

    [Fact]
    public async Task Clearing_works_after_a_restart_too()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync();
        await workspace.CreateProfileAsync(game);
        var restarted = workspace.Restart();

        var started = await restarted.Settings.ClearAllDataAsync();
        var job = await workspace.WaitForJobAsync(started.Value!.JobId);

        Assert.Equal(JobStatus.Succeeded, job.Status);
        Assert.Empty((await restarted.Games.ListAsync(workspace.Token)).Value!);
    }

    [Fact]
    public async Task A_moved_storage_is_cleared_where_it_is_and_the_root_goes_back_to_the_default()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync();
        var destination = Path.Combine(workspace.Root, "new-storage");
        var moved = await workspace.Settings.RelocateStorageAsync(destination);
        await workspace.WaitForJobAsync(moved.Value!.JobId);
        BackendWorkspace.WriteFile(Path.Combine(destination, "keep-me.txt"), "not savepoint's");

        var started = await workspace.Settings.ClearAllDataAsync();
        var job = await workspace.WaitForJobAsync(started.Value!.JobId);

        Assert.Equal(JobStatus.Succeeded, job.Status);
        Assert.False(Directory.Exists(Path.Combine(destination, game.Id)));
        Assert.True(File.Exists(Path.Combine(destination, "keep-me.txt")));
        Assert.Equal(workspace.DefaultStorageRoot, (await workspace.Backend.Services.Storage.GetAsync(workspace.Token)).Root);
    }

    [Fact]
    public async Task Leftover_history_of_games_that_are_no_longer_listed_goes_too_but_other_folders_stay()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync();
        var leftover = Path.Combine(workspace.DefaultStorageRoot, "removed-game");
        Directory.Move(Path.Combine(workspace.DefaultStorageRoot, game.Id), leftover);
        await workspace.AddGameAsync(workspace.NewGame("Beta"));
        BackendWorkspace.WriteFile(Path.Combine(workspace.DefaultStorageRoot, "my-documents", "notes.txt"), "not savepoint's");

        var started = await workspace.Settings.ClearAllDataAsync();
        await workspace.WaitForJobAsync(started.Value!.JobId);

        Assert.False(Directory.Exists(leftover));
        Assert.True(File.Exists(Path.Combine(workspace.DefaultStorageRoot, "my-documents", "notes.txt")));
    }

    [Fact]
    public async Task What_could_not_be_removed_is_listed_and_the_result_is_partial()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "only Windows refuses to delete an open file");
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync();
        var held = Path.Combine(workspace.DefaultStorageRoot, game.Id, "install.git", "HEAD");
        await using var open = new FileStream(held, FileMode.Open, FileAccess.Read, FileShare.Read);

        var started = await workspace.Settings.ClearAllDataAsync();
        var job = await workspace.WaitForJobAsync(started.Value!.JobId, TimeSpan.FromSeconds(180));

        Assert.Equal(JobStatus.Partial, job.Status);
        var result = Assert.IsType<ClearAllDataResult>(job.Result);
        Assert.NotEmpty(result.NotRemoved);
        Assert.All(result.NotRemoved, failure => Assert.False(string.IsNullOrEmpty(failure.Error.MessageId)));
        Assert.Empty((await workspace.Games.ListAsync(workspace.Token)).Value!);
    }
}
