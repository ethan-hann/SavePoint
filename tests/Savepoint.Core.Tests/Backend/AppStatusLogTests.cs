using Savepoint.Core.Catalog;
using Savepoint.Core.Git;
using Savepoint.Testing;

namespace Savepoint.Core.Tests.Backend;

public class AppStatusLogTests
{
    private readonly RecordingDiagnosticLog _log = new();

    [Fact]
    public async Task A_healthy_check_logs_the_git_version_the_catalog_and_the_storage_folder()
    {
        using var workspace = new BackendWorkspace(options => options with { Log = _log });

        var status = (await workspace.AppStatus.CheckAsync(workspace.Token)).Value!;

        var git = Assert.Single(_log.Containing("Startup check: git"));
        Assert.Equal(LogLevel.Information, git.Level);
        Assert.Contains(status.Git.Version!, git.Message, StringComparison.Ordinal);
        Assert.Contains(status.Git.Path, git.Message, StringComparison.Ordinal);
        var catalog = Assert.Single(_log.Containing("Startup check: the catalog"));
        Assert.Equal(LogLevel.Information, catalog.Level);
        Assert.Contains(workspace.CatalogDirectory, catalog.Message, StringComparison.Ordinal);
        var storage = Assert.Single(_log.Containing("Startup check: storage folder"));
        Assert.Contains(workspace.DefaultStorageRoot, storage.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_missing_git_is_an_error_that_says_where_it_looked_and_keeps_the_exception()
    {
        var missing = Path.Combine(Path.GetTempPath(), "savepoint-no-git-" + Guid.NewGuid().ToString("N"));
        using var workspace = new BackendWorkspace(options => options with { Log = _log, Git = GitBinary.At(missing, Path.Combine(missing, "config")) });

        await workspace.AppStatus.CheckAsync(workspace.Token);

        var git = Assert.Single(_log.Containing("Startup check: git"));
        Assert.Equal(LogLevel.Error, git.Level);
        Assert.Contains(workspace.Backend.Services.Git.ExecutablePath, git.Message, StringComparison.Ordinal);
        Assert.Contains("error.git.unavailable", git.Message, StringComparison.Ordinal);
        Assert.IsType<Savepoint.Core.Errors.GitUnavailableException>(git.Exception);
        Directory.Delete(missing, recursive: true);
    }

    [Fact]
    public async Task A_corrupt_catalog_file_is_a_warning_with_its_path_and_whether_a_backup_exists()
    {
        using var workspace = new BackendWorkspace(options => options with { Log = _log });
        var games = Path.Combine(workspace.CatalogDirectory, CatalogStore.GamesFileName);
        Directory.CreateDirectory(workspace.CatalogDirectory);
        await File.WriteAllTextAsync(games, "{ broken", workspace.Token);

        await workspace.AppStatus.CheckAsync(workspace.Token);

        var corrupt = Assert.Single(_log.Containing("Startup check", games));
        Assert.Equal(LogLevel.Warning, corrupt.Level);
        Assert.Contains("error.catalog.corrupt", corrupt.Message, StringComparison.Ordinal);
        Assert.Contains("no backup", corrupt.Message, StringComparison.Ordinal);
        Assert.Empty(_log.Containing("Startup check: storage folder"));
    }

    [Fact]
    public async Task Leftover_locks_cleared_at_startup_are_logged_with_the_repository_and_how_many()
    {
        using var workspace = new BackendWorkspace(options => options with { Log = _log });
        var game = await workspace.AddGameAsync();
        var installGit = Path.Combine(workspace.DefaultStorageRoot, game.Id, "install.git");
        await File.WriteAllTextAsync(Path.Combine(installGit, "index.lock"), string.Empty, workspace.Token);
        await File.WriteAllTextAsync(Path.Combine(installGit, "HEAD.lock"), string.Empty, workspace.Token);

        await workspace.Restart().AppStatus.CheckAsync(workspace.Token);

        var swept = Assert.Single(_log.Containing("Startup check", installGit));
        Assert.Equal(LogLevel.Information, swept.Level);
        Assert.Contains("2 ", swept.Message, StringComparison.Ordinal);
    }
}
