using Savepoint.Core.Backend;
using Savepoint.Core.Catalog;
using Savepoint.Core.Platform;
using Savepoint.Core.Services.Storage;

namespace Savepoint.Core.Tests.Backend;

// Earlier builds kept the default storage and logs in a folder the Windows installer now owns and clears. Starting the new build
// moves what is still there, once, and never touches a folder the player chose.
public class LegacyLocalDataTests
{
    [Fact]
    public async Task Saved_history_in_the_old_default_folder_moves_to_the_new_default_and_the_settings_follow()
    {
        using var world = new World();
        var game = await world.SeedHistoryAsync();

        world.Upgrade();
        var status = await world.Workspace.AppStatus.CheckAsync(world.Workspace.Token);

        Assert.True(status.Ok, status.Error?.Detail);
        Assert.True(status.Value.Storage?.Reachable);
        Assert.Equal(world.NewStorage, status.Value.Storage?.Root);
        Assert.False(Directory.Exists(world.OldStorage), "the old folder is still there");
        Assert.True(StorageContent.HoldsHistory(Path.Combine(world.NewStorage, game.Id)), "the history did not arrive");
        Assert.Equal(world.NewStorage, await world.SavedRootAsync());
    }

    [Fact]
    public async Task With_no_saved_folder_the_old_default_is_still_moved_so_a_first_run_does_not_hide_it()
    {
        using var world = new World();
        var game = await world.SeedHistoryAsync();
        File.Delete(Path.Combine(world.Workspace.CatalogDirectory, CatalogStore.SettingsFileName));

        world.Upgrade();
        var status = await world.Workspace.AppStatus.CheckAsync(world.Workspace.Token);

        Assert.True(status.Ok, status.Error?.Detail);
        Assert.True(StorageContent.HoldsHistory(Path.Combine(world.NewStorage, game.Id)), "the history did not arrive");
        Assert.False(Directory.Exists(world.OldStorage));
    }

    [Fact]
    public async Task A_folder_the_player_chose_is_never_moved_even_when_the_old_default_holds_files()
    {
        using var world = new World();
        var chosen = Path.Combine(world.Workspace.Root, "chosen");
        Directory.CreateDirectory(chosen);
        BackendWorkspace.WriteFile(Path.Combine(world.OldStorage, "stray", "note.txt"), "kept");
        await world.SaveRootAsync(chosen);

        world.Upgrade();
        await world.Workspace.AppStatus.CheckAsync(world.Workspace.Token);

        Assert.True(File.Exists(Path.Combine(world.OldStorage, "stray", "note.txt")));
        Assert.False(Directory.Exists(world.NewStorage));
        Assert.Equal(chosen, await world.SavedRootAsync());
    }

    [Fact]
    public async Task A_new_default_folder_that_already_exists_is_left_alone_with_the_old_one()
    {
        using var world = new World();
        BackendWorkspace.WriteFile(Path.Combine(world.OldStorage, "stray", "note.txt"), "old");
        BackendWorkspace.WriteFile(Path.Combine(world.NewStorage, "other", "note.txt"), "new");
        await world.SaveRootAsync(world.OldStorage);

        world.Upgrade();
        await world.Workspace.AppStatus.CheckAsync(world.Workspace.Token);

        Assert.Equal("old", File.ReadAllText(Path.Combine(world.OldStorage, "stray", "note.txt")));
        Assert.Equal("new", File.ReadAllText(Path.Combine(world.NewStorage, "other", "note.txt")));
        Assert.Equal(world.OldStorage, await world.SavedRootAsync());
    }

    [Fact]
    public async Task An_empty_or_missing_old_default_folder_moves_nothing()
    {
        using var world = new World();
        Directory.CreateDirectory(world.OldStorage);
        await world.SaveRootAsync(world.OldStorage);

        world.Upgrade();
        await world.Workspace.AppStatus.CheckAsync(world.Workspace.Token);

        Assert.False(Directory.Exists(world.NewStorage));
        Assert.True(Directory.Exists(world.OldStorage));
        Assert.Equal(world.OldStorage, await world.SavedRootAsync());
    }

    [Fact]
    public async Task A_move_that_cannot_be_done_keeps_the_app_on_the_old_folder_even_with_no_saved_folder()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Only Windows refuses to rename a folder that holds an open file.");
        using var world = new World();
        BackendWorkspace.WriteFile(Path.Combine(world.OldStorage, "stray", "held.bin"), "held");
        world.Upgrade();

        using (new FileStream(Path.Combine(world.OldStorage, "stray", "held.bin"), FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var status = await world.Workspace.AppStatus.CheckAsync(world.Workspace.Token);

            Assert.True(status.Ok, status.Error?.Detail);
        }

        Assert.True(File.Exists(Path.Combine(world.OldStorage, "stray", "held.bin")));
        Assert.False(Directory.Exists(world.NewStorage) && Directory.EnumerateFileSystemEntries(world.NewStorage).Any());
        Assert.Equal(world.OldStorage, await world.SavedRootAsync());
    }

    [Fact]
    public async Task Old_log_files_move_to_the_new_log_folder_and_a_file_already_there_is_not_overwritten()
    {
        using var world = new World();
        BackendWorkspace.WriteFile(Path.Combine(world.OldLogs, "savepoint-20260101.log"), "old one");
        BackendWorkspace.WriteFile(Path.Combine(world.OldLogs, "savepoint-20260102.log"), "old two");
        BackendWorkspace.WriteFile(Path.Combine(world.NewLogs, "savepoint-20260102.log"), "current");

        world.Upgrade();
        await world.Workspace.AppStatus.CheckAsync(world.Workspace.Token);

        Assert.True(File.Exists(Path.Combine(world.NewLogs, "savepoint-20260101.log")), "the old log did not arrive");
        Assert.Equal("old one", File.ReadAllText(Path.Combine(world.NewLogs, "savepoint-20260101.log")));
        Assert.Equal("current", File.ReadAllText(Path.Combine(world.NewLogs, "savepoint-20260102.log")));
        Assert.Equal("old two", File.ReadAllText(Path.Combine(world.OldLogs, "savepoint-20260102.log")));
        Assert.False(File.Exists(Path.Combine(world.OldLogs, "savepoint-20260101.log")));
    }

    [Fact]
    public async Task A_platform_with_no_old_folder_moves_nothing()
    {
        using var world = new World();
        BackendWorkspace.WriteFile(Path.Combine(world.OldStorage, "stray", "note.txt"), "kept");

        // Never upgraded: the platform has no legacy folder, as on Linux and macOS.
        await world.Workspace.AppStatus.CheckAsync(world.Workspace.Token);

        Assert.True(File.Exists(Path.Combine(world.OldStorage, "stray", "note.txt")));
    }

    // A backend whose platform answers with the folders an earlier build used until Upgrade, then with the new ones.
    private sealed class World : IDisposable
    {
        private bool _upgraded;

        public World()
        {
            Workspace = new BackendWorkspace(options => options with { Platform = PlatformFor(options) });
        }

        public BackendWorkspace Workspace { get; }

        public string Legacy => Path.Combine(Workspace.Root, "legacy");

        public string OldStorage => Path.Combine(Legacy, "Storage");

        public string OldLogs => Path.Combine(Legacy, "Logs");

        public string NewStorage => Path.Combine(Workspace.Root, "data", "Storage");

        public string NewLogs => Path.Combine(Workspace.Root, "data", "Logs");

        public void Upgrade()
        {
            _upgraded = true;
            Workspace.Restart();
        }

        // A game with one captured profile, saved while the old folders were the defaults.
        public async Task<GameDefinition> SeedHistoryAsync()
        {
            var status = await Workspace.AppStatus.CheckAsync(Workspace.Token);
            Assert.True(status.Ok, status.Error?.Detail);
            var game = await Workspace.AddGameAsync();
            await Workspace.CreateProfileAsync(game);
            Assert.True(StorageContent.HoldsHistory(Path.Combine(OldStorage, game.Id)));
            return game;
        }

        // Settings.SaveAsync keeps the storage root, which only a move changes; the file is written as an earlier run left it.
        public async Task SaveRootAsync(string root) =>
            await Workspace.Backend.Services.Catalog.SaveSettingsAsync(AppSettings.Defaults(root), Workspace.Token);

        public async Task<string> SavedRootAsync()
        {
            var settings = await Workspace.Settings.GetAsync(Workspace.Token);
            Assert.True(settings.Ok, settings.Error?.Detail);
            return settings.Value!.StorageRoot;
        }

        public void Dispose() => Workspace.Dispose();

        private IPlatform PlatformFor(BackendOptions options)
        {
            var root = Path.GetDirectoryName(options.InstallDirectory)!;
            var inner = options.Platform!;
            var directories = _upgraded
                ? inner.Directories with
                {
                    DefaultStorageRoot = Path.Combine(root, "data", "Storage"),
                    LogsDir = Path.Combine(root, "data", "Logs"),
                    LegacyLocalDir = Path.Combine(root, "legacy"),
                }
                : inner.Directories with
                {
                    DefaultStorageRoot = Path.Combine(root, "legacy", "Storage"),
                    LogsDir = Path.Combine(root, "legacy", "Logs"),
                    LegacyLocalDir = null,
                };
            return new RelocatedPlatform(inner, directories);
        }
    }

    private sealed class RelocatedPlatform(IPlatform inner, AppDirectories directories) : IPlatform
    {
        public OsKind Os => inner.Os;

        public StringComparison PathComparison => inner.PathComparison;

        public AppDirectories Directories => directories;

        public IReadOnlyDictionary<string, string> PathTokens => inner.PathTokens;

        public IProcessInspector Processes => inner.Processes;

        public PathDenylist Denylist(IEnumerable<string> extraBlockedRoots) => inner.Denylist(extraBlockedRoots);
    }
}
