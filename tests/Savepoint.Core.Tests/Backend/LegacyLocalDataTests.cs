using Savepoint.Core.Backend;
using Savepoint.Core.Catalog;
using Savepoint.Core.Platform;
using Savepoint.Core.Services.Storage;
using Savepoint.Core.Tests.Platform;
using Savepoint.Testing;

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
        var before = await world.PointCountAsync(game);

        world.Upgrade();
        var status = await world.Workspace.AppStatus.CheckAsync(world.Workspace.Token);

        Assert.True(status.Ok, status.Error?.Detail);
        Assert.True(status.Value.Storage?.Reachable);
        Assert.False(status.Value.Storage?.InInstallerFolder);
        Assert.Equal(world.NewStorage, status.Value.Storage?.Root);
        Assert.False(Directory.Exists(world.OldStorage), "the old folder is still there");
        Assert.True(StorageContent.HoldsHistory(Path.Combine(world.NewStorage, game.Id)), "the history did not arrive");
        Assert.Equal(world.NewStorage, await world.SavedRootAsync());
        Assert.True(before > 0);
        Assert.Equal(before, await world.PointCountAsync(game));
    }

    // A crash between the rename and the settings write leaves the settings naming a folder that is gone.
    [Fact]
    public async Task Settings_naming_a_missing_old_folder_follow_history_that_already_sits_in_the_new_default()
    {
        using var world = new World();
        var game = await world.SeedHistoryAsync();
        var before = await world.PointCountAsync(game);
        Directory.CreateDirectory(Path.GetDirectoryName(world.NewStorage)!);
        BackendWorkspace.MoveFolder(world.OldStorage, world.NewStorage);

        world.Upgrade();
        var status = await world.Workspace.AppStatus.CheckAsync(world.Workspace.Token);

        Assert.True(status.Ok, status.Error?.Detail);
        Assert.True(status.Value.Storage?.Reachable, "the storage was reported lost");
        Assert.Equal(world.NewStorage, await world.SavedRootAsync());
        Assert.Equal(before, await world.PointCountAsync(game));
    }

    // The settings write fails after the rename, so the folder goes back and the app keeps the history where the settings point.
    [Fact]
    public async Task A_settings_write_that_fails_after_the_move_puts_the_storage_back()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Only Windows refuses to replace a file another program holds open.");
        using var world = new World();
        var game = await world.SeedHistoryAsync();
        var before = await world.PointCountAsync(game);
        world.Upgrade();

        var settingsFile = Path.Combine(world.Workspace.CatalogDirectory, CatalogStore.SettingsFileName);
        using (new FileStream(settingsFile, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var status = await world.Workspace.AppStatus.CheckAsync(world.Workspace.Token);

            Assert.True(status.Ok, status.Error?.Detail);
        }

        Assert.True(StorageContent.HoldsHistory(Path.Combine(world.OldStorage, game.Id)), "the storage did not go back");
        Assert.False(Directory.Exists(world.NewStorage));
        Assert.Equal(world.OldStorage, await world.SavedRootAsync());
        Assert.Equal(before, await world.PointCountAsync(game));
    }

    [Fact]
    public async Task With_no_saved_folder_an_empty_new_default_does_not_hide_the_old_history()
    {
        using var world = new World();
        var game = await world.SeedHistoryAsync();
        var before = await world.PointCountAsync(game);
        File.Delete(Path.Combine(world.Workspace.CatalogDirectory, CatalogStore.SettingsFileName));
        Directory.CreateDirectory(world.NewStorage);

        world.Upgrade();
        var status = await world.Workspace.AppStatus.CheckAsync(world.Workspace.Token);

        Assert.True(status.Ok, status.Error?.Detail);
        Assert.True(StorageContent.HoldsHistory(Path.Combine(world.NewStorage, game.Id)), "the history did not arrive");
        Assert.False(Directory.Exists(world.OldStorage));
        Assert.Equal(world.NewStorage, await world.SavedRootAsync());
        Assert.Equal(before, await world.PointCountAsync(game));
    }

    // The settings write took effect, but reading the file back failed (a scanner holding it): the move must not be undone.
    [Fact]
    public async Task A_settings_write_that_landed_but_cannot_be_read_back_leaves_the_storage_in_the_new_default()
    {
        var armed = false;
        string? settingsFile = null;
        using var world = new World(root =>
        {
            if (!armed)
            {
                return;
            }

            armed = false;
            var hold = new FileStream(settingsFile!, FileMode.Open, FileAccess.Read, FileShare.None);
            // Longer than the catalog's own retries, so the read-back in the write fails, and short enough to be read after them.
            _ = Task.Delay(1600).ContinueWith(_ => hold.Dispose(), TaskScheduler.Default);
            throw new IOException("the settings file is held right after the write");
        });
        var game = await world.SeedHistoryAsync();
        var before = await world.PointCountAsync(game);
        settingsFile = Path.Combine(world.Workspace.CatalogDirectory, CatalogStore.SettingsFileName);
        world.Upgrade();
        armed = true;

        var status = await world.Workspace.AppStatus.CheckAsync(world.Workspace.Token);

        Assert.True(status.Ok, status.Error?.Detail);
        Assert.True(StorageContent.HoldsHistory(Path.Combine(world.NewStorage, game.Id)), "the storage was moved back");
        Assert.False(Directory.Exists(world.OldStorage));
        Assert.Equal(world.NewStorage, await world.SavedRootAsync());
        Assert.Equal(before, await world.PointCountAsync(game));
    }

    // A failure that is none of the expected kinds still takes the folder back, then surfaces as an error.
    [Fact]
    public async Task An_unexpected_failure_of_the_settings_write_puts_the_storage_back_and_fails_the_check()
    {
        var armed = false;
        World? self = null;
        using var world = new World(_ =>
        {
            if (!armed)
            {
                return;
            }

            armed = false;

            // The write had not applied after all, and the failure is not an I/O error.
            Task.Run(() => self!.Workspace.Backend.Services.Catalog.UpdateSettingsAsync(current => current! with { StorageRoot = self.OldStorage })).GetAwaiter().GetResult();
            throw new InvalidOperationException("an unexpected failure");
        });
        self = world;
        var game = await world.SeedHistoryAsync();
        world.Upgrade();
        armed = true;

        var status = await world.Workspace.AppStatus.CheckAsync(world.Workspace.Token);

        Assert.False(status.Ok);
        Assert.True(StorageContent.HoldsHistory(Path.Combine(world.OldStorage, game.Id)), "the storage was not put back");
        Assert.False(Directory.Exists(world.NewStorage));
    }

    // The settings name the new default but it is gone or empty while the history is still in the old folder.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Settings_naming_a_new_default_that_holds_nothing_finish_the_move_from_the_old_folder(bool emptyFolderThere)
    {
        using var world = new World();
        var game = await world.SeedHistoryAsync();
        var before = await world.PointCountAsync(game);
        await world.SaveRootAsync(world.NewStorage);
        if (emptyFolderThere)
        {
            Directory.CreateDirectory(world.NewStorage);
        }

        world.Upgrade();
        var status = await world.Workspace.AppStatus.CheckAsync(world.Workspace.Token);

        Assert.True(status.Ok, status.Error?.Detail);
        Assert.True(status.Value.Storage?.Reachable, "the storage was reported lost");
        Assert.True(StorageContent.HoldsHistory(Path.Combine(world.NewStorage, game.Id)), "the history did not arrive");
        Assert.Equal(world.NewStorage, await world.SavedRootAsync());
        Assert.Equal(before, await world.PointCountAsync(game));
    }

    // Nothing may take the new default as the first run's folder while it would hide the old history without a word.
    [Fact]
    public async Task With_no_saved_folder_a_settings_file_that_cannot_be_written_stops_the_check_at_the_storage_screen()
    {
        var armed = false;
        World? self = null;
        using var world = new World(root =>
        {
            if (!armed)
            {
                return;
            }

            // The write did not take effect: there are no settings again.
            armed = false;
            File.Delete(Path.Combine(self!.Workspace.CatalogDirectory, CatalogStore.SettingsFileName));
            throw new IOException("the write failed");
        });
        self = world;
        var game = await world.SeedHistoryAsync();
        File.Delete(Path.Combine(world.Workspace.CatalogDirectory, CatalogStore.SettingsFileName));
        BackendWorkspace.WriteFile(Path.Combine(world.NewStorage, "unrelated", "note.txt"), "not history");

        world.Upgrade();
        armed = true;
        var status = await world.Workspace.AppStatus.CheckAsync(world.Workspace.Token);

        Assert.True(status.Ok, status.Error?.Detail);
        Assert.False(status.Value.Storage?.Reachable);
        Assert.Equal(world.OldStorage, status.Value.Storage?.Root);
        Assert.True(StorageContent.HoldsHistory(Path.Combine(world.OldStorage, game.Id)));
    }

    [Fact]
    public async Task With_no_saved_folder_history_in_both_folders_keeps_the_old_one_in_use_and_flagged_and_says_so()
    {
        using var world = new World();
        var game = await world.SeedHistoryAsync();
        File.Delete(Path.Combine(world.Workspace.CatalogDirectory, CatalogStore.SettingsFileName));
        CopyTree(world.OldStorage, world.NewStorage);

        world.Upgrade();
        var status = await world.Workspace.AppStatus.CheckAsync(world.Workspace.Token);

        Assert.True(status.Ok, status.Error?.Detail);
        Assert.Equal(world.OldStorage, await world.SavedRootAsync());
        Assert.True(status.Value.Storage?.InInstallerFolder);
        Assert.True(StorageContent.HoldsHistory(Path.Combine(world.NewStorage, game.Id)));
        Assert.Contains(world.Log.Entries, entry => entry.Level == LogLevel.Warning && entry.Message.Contains("both hold history", StringComparison.Ordinal));
    }

    [Fact]
    public async Task With_no_saved_folder_a_new_default_that_cannot_take_the_move_leaves_the_history_in_use_and_flagged()
    {
        using var world = new World();
        var game = await world.SeedHistoryAsync();
        File.Delete(Path.Combine(world.Workspace.CatalogDirectory, CatalogStore.SettingsFileName));
        BackendWorkspace.WriteFile(Path.Combine(world.NewStorage, "unrelated", "note.txt"), "not history");

        world.Upgrade();
        var status = await world.Workspace.AppStatus.CheckAsync(world.Workspace.Token);

        Assert.True(status.Ok, status.Error?.Detail);
        Assert.True(StorageContent.HoldsHistory(Path.Combine(world.OldStorage, game.Id)));
        Assert.Equal(world.OldStorage, await world.SavedRootAsync());
        Assert.True(status.Value.Storage?.InInstallerFolder);
        Assert.True(File.Exists(Path.Combine(world.NewStorage, "unrelated", "note.txt")));
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
        Assert.Contains(world.Log.Entries, entry => entry.Level == LogLevel.Warning && entry.Message.Contains("stays where it is", StringComparison.Ordinal));
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

            // Left where the installer clears it, so the startup notice and the Settings screen are told.
            Assert.True(status.Value.Storage?.InInstallerFolder);
            var summary = await world.Workspace.Settings.GetStorageSummaryAsync(world.Workspace.Token);
            Assert.True(summary.Value?.InInstallerFolder);
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

    private static void CopyTree(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var directory in Directory.EnumerateDirectories(from, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(Path.Combine(to, Path.GetRelativePath(from, directory)));
        }

        foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            File.Copy(file, Path.Combine(to, Path.GetRelativePath(from, file)));
        }
    }

    // A backend whose platform answers with the folders an earlier build used until Upgrade, then with the new ones.
    private sealed class World : IDisposable
    {
        private bool _upgraded;

        // afterSettings runs inside every settings write the startup check makes for a storage root.
        public World(Action<string>? afterSettings = null)
        {
            Workspace = new BackendWorkspace(options => options with { Platform = PlatformFor(options), Log = Log, AfterRelocationSettings = afterSettings });
        }

        public RecordingDiagnosticLog Log { get; } = new();

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

        public async Task<int> PointCountAsync(GameDefinition game)
        {
            var history = await Workspace.History.GetAsync(game.Id, null, Workspace.Token);
            Assert.True(history.Ok, history.Error?.Detail);
            return history.Value!.Points.Count;
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
}
