using Savepoint.Core.Api;
using Savepoint.Core.Catalog;

namespace Savepoint.Core.Tests.Backend;

// A storage folder that is gone is unreachable, never empty (D31).
public class StorageUnreachableTests
{
    private static async Task<(BackendWorkspace Workspace, GameDefinition Game)> WithHistoryAsync()
    {
        var workspace = new BackendWorkspace();
        try
        {
            // The startup check is what makes the first root a saved one.
            await workspace.AppStatus.CheckAsync(workspace.Token);
            var game = await workspace.AddGameAsync();
            await workspace.CreateProfileAsync(game);
            return (workspace, game);
        }
        catch
        {
            workspace.Dispose();
            throw;
        }
    }

    [Fact]
    public async Task The_first_startup_check_creates_the_storage_folder_and_remembers_it()
    {
        using var workspace = new BackendWorkspace();

        var status = await workspace.AppStatus.CheckAsync(workspace.Token);

        Assert.True(status.Ok, status.Error?.Detail);
        Assert.True(Directory.Exists(workspace.DefaultStorageRoot));
        var settings = Path.Combine(workspace.CatalogDirectory, "settings.json");
        Assert.True(File.Exists(settings), "the first root was not saved");
        Assert.Contains("storageRoot", await File.ReadAllTextAsync(settings, workspace.Token));
    }

    [Fact]
    public async Task A_storage_folder_that_has_gone_missing_is_unreachable_for_history_and_a_finding_of_the_startup_check()
    {
        var (workspace, game) = await WithHistoryAsync();
        using var _ = workspace;
        var away = workspace.DefaultStorageRoot + "-away";
        Directory.Move(workspace.DefaultStorageRoot, away);

        var history = await workspace.History.GetAsync(game.Id, null, workspace.Token);
        var restarted = workspace.Restart();
        var check = await restarted.AppStatus.CheckAsync(workspace.Token);

        Assert.Equal("error.storage.unreachable", history.Error?.MessageId);
        Assert.True(check.Ok, check.Error?.Detail);
        Assert.True(check.Value.Git.Available);
        var storage = Assert.IsType<StorageStatus>(check.Value.Storage);
        Assert.False(storage.Reachable);
        Assert.Equal(workspace.DefaultStorageRoot, storage.Root);
        Assert.Equal("error.storage.unreachable", storage.Error?.MessageId);
        Assert.False(Directory.Exists(workspace.DefaultStorageRoot), "an unreachable storage folder must not be made again");

        Directory.Move(away, workspace.DefaultStorageRoot);
        Assert.Single((await workspace.History.GetAsync(game.Id, null, workspace.Token)).Value!.Points);
    }

    [Fact]
    public async Task A_move_from_a_storage_folder_that_has_gone_missing_is_refused_and_settings_stay()
    {
        var (workspace, _) = await WithHistoryAsync();
        using var _ = workspace;
        Directory.Move(workspace.DefaultStorageRoot, workspace.DefaultStorageRoot + "-away");
        var destination = Path.Combine(workspace.Root, "new-storage");

        var started = await workspace.Settings.RelocateStorageAsync(destination);

        Assert.Equal("error.storage.unreachable", started.Error?.MessageId);
        Assert.False(Directory.Exists(destination));
        Assert.Equal(workspace.DefaultStorageRoot, (await workspace.Settings.GetAsync(workspace.Token)).Value!.StorageRoot);
    }

    [Fact]
    public async Task A_move_from_a_storage_folder_that_is_there_but_holds_no_game_folders_is_refused()
    {
        var (workspace, _) = await WithHistoryAsync();
        using var _ = workspace;
        foreach (var folder in Directory.GetDirectories(workspace.DefaultStorageRoot))
        {
            Savepoint.Core.Tests.Git.GitWorkspace.DeleteDirectory(folder);
        }

        var started = await workspace.Settings.RelocateStorageAsync(Path.Combine(workspace.Root, "new-storage"));

        Assert.Equal("error.storage.unreachable", started.Error?.MessageId);
        Assert.Equal(workspace.DefaultStorageRoot, (await workspace.Settings.GetAsync(workspace.Token)).Value!.StorageRoot);
    }

    [Fact]
    public async Task A_game_cannot_be_deleted_while_the_storage_folder_is_unreachable()
    {
        var (workspace, game) = await WithHistoryAsync();
        using var _ = workspace;
        Directory.Move(workspace.DefaultStorageRoot, workspace.DefaultStorageRoot + "-away");

        var deleted = await workspace.Games.DeleteAsync(game.Id);

        Assert.Equal("error.storage.unreachable", deleted.Error?.MessageId);
        Assert.Single((await workspace.Games.ListAsync(workspace.Token)).Value!);
        Assert.IsType<Savepoint.Core.Paths.ConsentMarkerState.Valid>(Savepoint.Core.Paths.ConsentMarker.Read(game.Install.Path));
    }
}
