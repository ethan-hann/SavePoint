using Savepoint.Core.Api;

using Savepoint.Core.Catalog;
using Savepoint.Core.Git;

namespace Savepoint.Core.Tests.Backend;

public class AppStatusTests
{
    [Fact]
    public async Task A_healthy_install_reports_git_and_a_clean_catalog()
    {
        using var workspace = new BackendWorkspace();

        var status = await workspace.AppStatus.CheckAsync(workspace.Token);

        Assert.True(status.Ok, status.Error?.Detail);
        Assert.True(status.Value.Git.Available);
        Assert.Null(status.Value.Git.Error);
        Assert.Equal(workspace.Backend.Services.Git.ExecutablePath, status.Value.Git.Path);
        Assert.StartsWith("2.", status.Value.Git.Version, StringComparison.Ordinal);
        Assert.Equal(workspace.CatalogDirectory, status.Value.Catalog.Directory);
        Assert.Empty(status.Value.Catalog.CorruptFiles);
    }

    [Fact]
    public async Task The_check_makes_sure_the_storage_folder_exists()
    {
        using var workspace = new BackendWorkspace();

        await workspace.AppStatus.CheckAsync(workspace.Token);

        Assert.True(Directory.Exists(workspace.DefaultStorageRoot));
    }

    [Fact]
    public async Task A_missing_git_is_a_finding_not_a_failure_and_names_where_it_looked()
    {
        var missing = Path.Combine(Path.GetTempPath(), "savepoint-no-git-" + Guid.NewGuid().ToString("N"));
        using var workspace = new BackendWorkspace(options => options with { Git = GitBinary.At(missing, Path.Combine(missing, "config")) });

        var status = await workspace.AppStatus.CheckAsync(workspace.Token);

        Assert.True(status.Ok, status.Error?.Detail);
        Assert.False(status.Value.Git.Available);
        Assert.Equal("error.git.unavailable", status.Value.Git.Error?.MessageId);
        Assert.Equal(workspace.Backend.Services.Git.ExecutablePath, status.Value.Git.Path);
        Assert.Null(status.Value.Git.Version);
        Directory.Delete(missing, recursive: true);
    }

    [Fact]
    public async Task Each_unreadable_catalog_file_is_listed_with_its_backup_when_there_is_a_good_one()
    {
        using var workspace = new BackendWorkspace();
        await workspace.AddGameAsync();
        await workspace.Settings.SaveAsync(AppSettings.Defaults(workspace.DefaultStorageRoot));
        await workspace.Settings.SaveAsync(AppSettings.Defaults(workspace.DefaultStorageRoot) with { Theme = ThemePreference.Dark });
        var games = Path.Combine(workspace.CatalogDirectory, CatalogStore.GamesFileName);
        var settings = Path.Combine(workspace.CatalogDirectory, CatalogStore.SettingsFileName);
        await File.WriteAllTextAsync(games, "{ broken", workspace.Token);
        await File.WriteAllTextAsync(settings, "[1,2", workspace.Token);
        await File.WriteAllTextAsync(settings + ".bak", "also broken", workspace.Token);

        var status = (await workspace.AppStatus.CheckAsync(workspace.Token)).Value!;

        Assert.False(status.Git.Available is false);
        Assert.Equal([games, settings], status.Catalog.CorruptFiles.Select(f => f.Path).Order());
        Assert.All(status.Catalog.CorruptFiles, file => Assert.Equal("error.catalog.corrupt", file.Error.MessageId));
        Assert.Null(status.Catalog.CorruptFiles.Single(f => f.Path == settings).BackupPath);
    }

    [Fact]
    public async Task A_readable_backup_is_offered_for_a_corrupt_file()
    {
        using var workspace = new BackendWorkspace();
        await workspace.AddGameAsync(workspace.NewGame("Alpha"));
        await workspace.AddGameAsync(workspace.NewGame("Beta"));
        var games = Path.Combine(workspace.CatalogDirectory, CatalogStore.GamesFileName);
        await File.WriteAllTextAsync(games, "{ broken", workspace.Token);

        var file = Assert.Single((await workspace.AppStatus.CheckAsync(workspace.Token)).Value!.Catalog.CorruptFiles);

        Assert.Equal(games + ".bak", file.BackupPath);
    }

    [Fact]
    public async Task Restoring_the_backup_moves_the_corrupt_file_aside_and_brings_the_good_one_back()
    {
        using var workspace = new BackendWorkspace();
        await workspace.AddGameAsync(workspace.NewGame("Alpha"));
        await workspace.AddGameAsync(workspace.NewGame("Beta"));
        var games = Path.Combine(workspace.CatalogDirectory, CatalogStore.GamesFileName);
        await File.WriteAllTextAsync(games, "{ broken", workspace.Token);

        var recovered = await workspace.AppStatus.RecoverCatalogFileAsync(games, CatalogRecovery.RestoreBackup, workspace.Token);

        Assert.True(recovered.Ok, recovered.Error?.Detail);
        Assert.Equal("{ broken", await File.ReadAllTextAsync(recovered.Value, workspace.Token));
        Assert.NotEqual(games, recovered.Value);
        Assert.Equal(Path.GetDirectoryName(games), Path.GetDirectoryName(recovered.Value));
        Assert.Equal(["Alpha"], (await workspace.Games.ListAsync(workspace.Token)).Value!.Select(g => g.Name));
        Assert.Empty((await workspace.AppStatus.CheckAsync(workspace.Token)).Value!.Catalog.CorruptFiles);
    }

    [Fact]
    public async Task Starting_empty_moves_the_corrupt_file_aside_and_leaves_the_catalog_empty()
    {
        using var workspace = new BackendWorkspace();
        await workspace.AddGameAsync();
        var games = Path.Combine(workspace.CatalogDirectory, CatalogStore.GamesFileName);
        await File.WriteAllTextAsync(games, "{ broken", workspace.Token);

        var recovered = await workspace.AppStatus.RecoverCatalogFileAsync(games, CatalogRecovery.StartEmpty, workspace.Token);

        Assert.True(recovered.Ok, recovered.Error?.Detail);
        Assert.Equal("{ broken", await File.ReadAllTextAsync(recovered.Value, workspace.Token));
        Assert.False(File.Exists(games));
        Assert.Empty((await workspace.Games.ListAsync(workspace.Token)).Value!);
        Assert.Empty((await workspace.AppStatus.CheckAsync(workspace.Token)).Value!.Catalog.CorruptFiles);
    }

    [Fact]
    public async Task Restoring_when_there_is_no_backup_or_it_is_broken_moves_nothing()
    {
        using var workspace = new BackendWorkspace();
        await workspace.AddGameAsync();
        var games = Path.Combine(workspace.CatalogDirectory, CatalogStore.GamesFileName);
        await File.WriteAllTextAsync(games, "{ broken", workspace.Token);
        File.Delete(games + ".bak");

        var none = await workspace.AppStatus.RecoverCatalogFileAsync(games, CatalogRecovery.RestoreBackup, workspace.Token);
        await File.WriteAllTextAsync(games + ".bak", "also { broken", workspace.Token);
        var broken = await workspace.AppStatus.RecoverCatalogFileAsync(games, CatalogRecovery.RestoreBackup, workspace.Token);

        Assert.False(none.Ok);
        Assert.False(broken.Ok);
        Assert.Equal("{ broken", await File.ReadAllTextAsync(games, workspace.Token));
        Assert.Equal(2, Directory.EnumerateFiles(workspace.CatalogDirectory).Count(f => !f.Contains(".corrupt-", StringComparison.Ordinal) && Path.GetFileName(f).StartsWith("games.json", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Only_catalog_files_in_the_catalog_folder_can_be_recovered()
    {
        using var workspace = new BackendWorkspace();
        await workspace.AddGameAsync();
        var stray = Path.Combine(workspace.Root, "games.json");
        await File.WriteAllTextAsync(stray, "{ broken", workspace.Token);

        var elsewhere = await workspace.AppStatus.RecoverCatalogFileAsync(stray, CatalogRecovery.StartEmpty, workspace.Token);
        var notCatalog = await workspace.AppStatus.RecoverCatalogFileAsync(Path.Combine(workspace.CatalogDirectory, "other.json"), CatalogRecovery.StartEmpty, workspace.Token);

        Assert.False(elsewhere.Ok);
        Assert.False(notCatalog.Ok);
        Assert.True(File.Exists(stray));
    }

    [Fact]
    public async Task Recovering_a_file_that_is_not_corrupt_changes_nothing()
    {
        using var workspace = new BackendWorkspace();
        await workspace.AddGameAsync();
        var games = Path.Combine(workspace.CatalogDirectory, CatalogStore.GamesFileName);

        var recovered = await workspace.AppStatus.RecoverCatalogFileAsync(games, CatalogRecovery.StartEmpty, workspace.Token);

        Assert.False(recovered.Ok);
        Assert.Single((await workspace.Games.ListAsync(workspace.Token)).Value!);
    }

    [Fact]
    public async Task A_storage_folder_that_cannot_be_made_fails_the_check_with_the_path()
    {
        using var workspace = new BackendWorkspace();
        var blocker = Path.Combine(workspace.Root, "a-file");
        await File.WriteAllTextAsync(blocker, "not a folder", workspace.Token);
        var root = Path.Combine(blocker, "storage");
        await workspace.Backend.Services.Catalog.SaveSettingsAsync(new AppSettings(StorageRoot: root), workspace.Token);

        var status = await workspace.AppStatus.CheckAsync(workspace.Token);

        Assert.Equal("error.storage.unreachable", status.Error?.MessageId);
        Assert.Equal(root, status.Error!.Args["path"]);
    }

    [Fact]
    public async Task Leftover_lock_files_from_an_earlier_run_are_cleared_at_startup()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync();
        var installLock = Path.Combine(workspace.DefaultStorageRoot, game.Id, "install.git", "index.lock");
        var savesLock = Path.Combine(workspace.DefaultStorageRoot, game.Id, "external.git", "HEAD.lock");
        await File.WriteAllTextAsync(installLock, string.Empty, workspace.Token);
        await File.WriteAllTextAsync(savesLock, string.Empty, workspace.Token);

        var restarted = workspace.Restart();
        await restarted.AppStatus.CheckAsync(workspace.Token);

        Assert.False(File.Exists(installLock));
        Assert.False(File.Exists(savesLock));
    }

    [Fact]
    public async Task A_corrupt_catalog_blocks_the_sweep_and_nothing_is_touched()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync();
        var installLock = Path.Combine(workspace.DefaultStorageRoot, game.Id, "install.git", "index.lock");
        await File.WriteAllTextAsync(installLock, string.Empty, workspace.Token);
        await File.WriteAllTextAsync(Path.Combine(workspace.CatalogDirectory, CatalogStore.GamesFileName), "{ broken", workspace.Token);

        var status = await workspace.Restart().AppStatus.CheckAsync(workspace.Token);

        Assert.Single(status.Value!.Catalog.CorruptFiles);
        Assert.True(File.Exists(installLock));
    }

    [Fact]
    public async Task Lock_files_are_only_swept_once_so_a_later_check_never_touches_a_running_save()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync();
        await workspace.AppStatus.CheckAsync(workspace.Token);
        var installLock = Path.Combine(workspace.DefaultStorageRoot, game.Id, "install.git", "index.lock");
        await File.WriteAllTextAsync(installLock, string.Empty, workspace.Token);

        await workspace.AppStatus.CheckAsync(workspace.Token);

        Assert.True(File.Exists(installLock));
    }

    [Fact]
    public async Task A_cancelled_check_answers_the_cancelled_message()
    {
        using var workspace = new BackendWorkspace();
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();

        Assert.Equal("error.operation.cancelled", (await workspace.AppStatus.CheckAsync(cancelled.Token)).Error?.MessageId);
    }
}
