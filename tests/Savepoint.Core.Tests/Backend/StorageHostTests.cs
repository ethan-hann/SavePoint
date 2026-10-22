using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;

namespace Savepoint.Core.Tests.Backend;

public class StorageHostTests
{
    [Fact]
    public async Task With_no_settings_the_platforms_default_root_is_used()
    {
        using var workspace = new BackendWorkspace();

        var snapshot = await workspace.Backend.Services.Storage.GetAsync(workspace.Token);

        Assert.Equal(workspace.DefaultStorageRoot, snapshot.Root);
    }

    [Fact]
    public async Task The_root_saved_in_the_settings_is_used()
    {
        using var workspace = new BackendWorkspace();
        var chosen = Path.Combine(workspace.Root, "elsewhere", "storage");
        Directory.CreateDirectory(chosen);
        await workspace.Backend.Services.Catalog.SaveSettingsAsync(new AppSettings(StorageRoot: chosen), workspace.Token);

        var snapshot = await workspace.Backend.Services.Storage.GetAsync(workspace.Token);

        Assert.Equal(chosen, snapshot.Root);
    }

    [Fact]
    public async Task The_snapshot_is_built_once_and_kept()
    {
        using var workspace = new BackendWorkspace();
        var storage = workspace.Backend.Services.Storage;

        var first = await storage.GetAsync(workspace.Token);
        var second = await storage.GetAsync(workspace.Token);

        Assert.Same(first, second);
    }

    [Fact]
    public async Task A_restarted_backend_reads_the_saved_root_again()
    {
        using var workspace = new BackendWorkspace();
        var chosen = Path.Combine(workspace.Root, "moved");
        Directory.CreateDirectory(chosen);
        await workspace.Backend.Services.Catalog.SaveSettingsAsync(new AppSettings(StorageRoot: chosen), workspace.Token);

        var restarted = workspace.Restart();

        Assert.Equal(chosen, (await restarted.Services.Storage.GetAsync(workspace.Token)).Root);
    }

    [Fact]
    public async Task A_corrupt_settings_file_stops_the_build_without_touching_the_file()
    {
        using var workspace = new BackendWorkspace();
        Directory.CreateDirectory(workspace.CatalogDirectory);
        var settings = Path.Combine(workspace.CatalogDirectory, CatalogStore.SettingsFileName);
        await File.WriteAllTextAsync(settings, "{ not json", workspace.Token);

        await Assert.ThrowsAsync<CatalogCorruptException>(() => workspace.Backend.Services.Storage.GetAsync(workspace.Token));

        Assert.Equal("{ not json", await File.ReadAllTextAsync(settings, workspace.Token));
    }

    [Fact]
    public async Task The_snapshot_names_the_git_directories_of_a_game_under_its_own_folder()
    {
        using var workspace = new BackendWorkspace();

        var snapshot = await workspace.Backend.Services.Storage.GetAsync(workspace.Token);

        Assert.Equal(Path.Combine(workspace.DefaultStorageRoot, "alpha-1", "install.git"), snapshot.GitDirectory("alpha-1", RepoRole.Install));
        Assert.Equal(Path.Combine(workspace.DefaultStorageRoot, "alpha-1", "external.git"), snapshot.GitDirectory("alpha-1", RepoRole.External));
    }

    [Fact]
    public async Task The_install_folder_of_the_app_is_protected_from_games()
    {
        using var workspace = new BackendWorkspace();
        var snapshot = await workspace.Backend.Services.Storage.GetAsync(workspace.Token);

        var refused = Assert.Throws<PathSafetyException>(() => snapshot.PathSafety.Check(workspace.InstallDirectory));

        Assert.Equal("error.pathSafety.tier1Blocked", refused.MessageId);
    }
}
