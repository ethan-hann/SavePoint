using Savepoint.Core.Backend;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;
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
        Assert.True(StorageContent.HoldsHistory(Path.Combine(world.NewStorage, game.Id)), "the history did not arrive: " + world.Warnings());
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
        world.DeleteSettings();
        Directory.CreateDirectory(world.NewStorage);

        world.Upgrade();
        var status = await world.Workspace.AppStatus.CheckAsync(world.Workspace.Token);

        Assert.True(status.Ok, status.Error?.Detail);
        Assert.True(StorageContent.HoldsHistory(Path.Combine(world.NewStorage, game.Id)), "the history did not arrive: " + world.Warnings());
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
        World? self = null;
        using var world = new World(root =>
        {
            if (!armed)
            {
                return;
            }

            armed = false;
            var hold = new FileStream(settingsFile!, FileMode.Open, FileAccess.Read, FileShare.None);
            _ = ReleaseOnceTheWriteGaveUpAsync(self!.Log, hold);
            throw new IOException("the settings file is held right after the write");
        });
        self = world;
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

        // It stayed because the re-read showed the settings name the new default, not because they could not be read.
        Assert.Contains(world.Log.Entries, entry => entry.Message.Contains("the settings name", StringComparison.Ordinal));
        Assert.DoesNotContain(world.Log.Entries, entry => entry.Message.Contains("could not be read, so", StringComparison.Ordinal));
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
            self!.RevertSettingsTo(self.OldStorage);
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
        Assert.True(StorageContent.HoldsHistory(Path.Combine(world.NewStorage, game.Id)), "the history did not arrive: " + world.Warnings());
        Assert.Equal(world.NewStorage, await world.SavedRootAsync());
        Assert.Equal(before, await world.PointCountAsync(game));
    }

    // Nothing may take the new default as the first run's folder while it would hide the old history without a word, and no screen
    // may be a dead end: the old folder is used for this run only, flagged, and nothing is written.
    [Fact]
    public async Task With_no_saved_folder_a_settings_file_that_cannot_be_written_uses_the_old_history_for_this_run_flagged()
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
            self!.DeleteSettings();
            throw new IOException("the write failed");
        });
        self = world;
        var game = await world.SeedHistoryAsync();
        var before = await world.PointCountAsync(game);
        world.DeleteSettings();
        BackendWorkspace.WriteFile(Path.Combine(world.NewStorage, "unrelated", "note.txt"), "not history");

        world.Upgrade();
        armed = true;
        var status = await world.Workspace.AppStatus.CheckAsync(world.Workspace.Token);

        Assert.True(status.Ok, status.Error?.Detail);
        Assert.True(status.Value.Storage?.Reachable);
        Assert.Equal(world.OldStorage, status.Value.Storage?.Root);
        Assert.True(status.Value.Storage?.InInstallerFolder);
        Assert.True(StorageContent.HoldsHistory(Path.Combine(world.OldStorage, game.Id)));
        Assert.Equal(before, await world.PointCountAsync(game));
        Assert.Equal(world.OldStorage, (await world.Workspace.Settings.GetStorageSummaryAsync(world.Workspace.Token)).Value!.StorageRoot);
        Assert.False(File.Exists(Path.Combine(world.Workspace.CatalogDirectory, CatalogStore.SettingsFileName)), "something was written to the settings");
    }

    // The settings name an old folder that is empty while the history sits in the new default; the write that fixes that fails.
    [Fact]
    public async Task Settings_that_cannot_follow_a_finished_move_show_the_new_default_for_this_run()
    {
        var armed = false;
        World? self = null;
        using var world = new World(root =>
        {
            if (!armed)
            {
                return;
            }

            armed = false;
            self!.RevertSettingsTo(self.OldStorage);
            throw new IOException("the write failed");
        });
        self = world;
        var game = await world.SeedHistoryAsync();
        var before = await world.PointCountAsync(game);
        Directory.CreateDirectory(Path.GetDirectoryName(world.NewStorage)!);
        BackendWorkspace.MoveFolder(world.OldStorage, world.NewStorage);
        Directory.CreateDirectory(world.OldStorage);

        world.Upgrade();
        armed = true;
        var status = await world.Workspace.AppStatus.CheckAsync(world.Workspace.Token);

        Assert.True(status.Ok, status.Error?.Detail);
        Assert.Equal(world.NewStorage, status.Value.Storage?.Root);
        Assert.False(status.Value.Storage?.InInstallerFolder);
        Assert.Equal(before, await world.PointCountAsync(game));
    }

    // The empty-start marker is SavePoint's own note in an otherwise empty folder, not data.
    [Fact]
    public async Task A_new_default_holding_only_the_empty_start_marker_is_moved_onto()
    {
        using var world = new World();
        var game = await world.SeedHistoryAsync();
        world.DeleteSettings();
        BackendWorkspace.WriteFile(Path.Combine(world.NewStorage, StorageContent.EmptyStartMarkerFileName), "empty start");

        world.Upgrade();
        var status = await world.Workspace.AppStatus.CheckAsync(world.Workspace.Token);

        Assert.True(status.Ok, status.Error?.Detail);
        Assert.True(StorageContent.HoldsHistory(Path.Combine(world.NewStorage, game.Id)), "the history did not arrive: " + world.Warnings());
        Assert.False(Directory.Exists(world.OldStorage));
        Assert.Equal(world.NewStorage, await world.SavedRootAsync());
    }

    [Fact]
    public async Task An_old_folder_holding_only_the_empty_start_marker_moves_nothing()
    {
        using var world = new World();
        BackendWorkspace.WriteFile(Path.Combine(world.OldStorage, StorageContent.EmptyStartMarkerFileName), "empty start");
        await world.SaveRootAsync(world.OldStorage);

        world.Upgrade();
        await world.Workspace.AppStatus.CheckAsync(world.Workspace.Token);

        Assert.False(Directory.Exists(world.NewStorage));
        Assert.True(File.Exists(Path.Combine(world.OldStorage, StorageContent.EmptyStartMarkerFileName)));
    }

    // A cancelled or broken read-back must not replace the failure that led to it.
    [Fact]
    public async Task A_cancelled_read_back_leaves_the_storage_in_the_new_default_and_the_original_failure_surfaces()
    {
        var armed = false;
        World? self = null;
        using var cancel = new CancellationTokenSource();
        using var world = new World(root =>
        {
            if (!armed)
            {
                return;
            }

            armed = false;
            self!.RevertSettingsTo(self.OldStorage);
            cancel.Cancel();
            throw new InvalidOperationException("the original failure");
        });
        self = world;
        var game = await world.SeedHistoryAsync();
        world.Upgrade();
        armed = true;

        var status = await world.Workspace.AppStatus.CheckAsync(cancel.Token);

        Assert.False(status.Ok);
        Assert.NotEqual("error.operation.cancelled", status.Error?.MessageId);
        Assert.True(StorageContent.HoldsHistory(Path.Combine(world.NewStorage, game.Id)), "the storage was moved back without knowing the settings");
        Assert.Contains(world.Log.Entries, entry => entry.Message.Contains("could not be read", StringComparison.Ordinal));
    }

    // The settings could not be read after the write, and the next start finds the folder in the new default and the settings behind it.
    [Fact]
    public async Task The_next_start_finishes_a_move_whose_read_back_was_unreadable()
    {
        var armed = false;
        World? self = null;
        using var cancel = new CancellationTokenSource();
        using var world = new World(root =>
        {
            if (!armed)
            {
                return;
            }

            armed = false;
            self!.RevertSettingsTo(self.OldStorage);
            cancel.Cancel();
            throw new IOException("the write failed");
        });
        self = world;
        var game = await world.SeedHistoryAsync();
        var before = await world.PointCountAsync(game);
        world.Upgrade();
        armed = true;
        await world.Workspace.AppStatus.CheckAsync(cancel.Token);

        world.Upgrade();
        var status = await world.Workspace.AppStatus.CheckAsync(world.Workspace.Token);

        Assert.True(status.Ok, status.Error?.Detail);
        Assert.Equal(world.NewStorage, status.Value.Storage?.Root);
        Assert.Equal(world.NewStorage, await world.SavedRootAsync());
        Assert.Equal(before, await world.PointCountAsync(game));
    }

    // The settings write fails and the folder cannot go back either: this run still sees the history, and the next start finishes it.
    [Fact]
    public async Task A_failed_move_back_shows_the_history_this_run_and_the_next_start_finishes_the_move()
    {
        var armed = false;
        World? self = null;
        using var world = new World(root =>
        {
            if (!armed)
            {
                return;
            }

            armed = false;
            self!.RevertSettingsTo(self.OldStorage);

            // Something took the old name, so the folder cannot return to it.
            Directory.CreateDirectory(self.OldStorage);
            throw new IOException("the write failed");
        });
        self = world;
        var game = await world.SeedHistoryAsync();
        var before = await world.PointCountAsync(game);
        world.Upgrade();
        armed = true;

        var first = await world.Workspace.AppStatus.CheckAsync(world.Workspace.Token);

        Assert.True(first.Ok, first.Error?.Detail);
        Assert.Equal(world.NewStorage, first.Value.Storage?.Root);
        Assert.Equal(before, await world.PointCountAsync(game));
        Assert.Contains(world.Log.Entries, entry => entry.Message.Contains("next start finishes the move", StringComparison.Ordinal));

        world.Upgrade();
        var second = await world.Workspace.AppStatus.CheckAsync(world.Workspace.Token);

        Assert.True(second.Ok, second.Error?.Detail);
        Assert.Equal(world.NewStorage, await world.SavedRootAsync());
        Assert.Equal(before, await world.PointCountAsync(game));
    }

    [Fact]
    public async Task With_no_saved_folder_history_in_both_folders_keeps_the_old_one_in_use_and_flagged_and_says_so()
    {
        using var world = new World();
        var game = await world.SeedHistoryAsync();
        world.DeleteSettings();
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
        world.DeleteSettings();
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
        world.DeleteSettings();

        world.Upgrade();
        var status = await world.Workspace.AppStatus.CheckAsync(world.Workspace.Token);

        Assert.True(status.Ok, status.Error?.Detail);
        Assert.True(StorageContent.HoldsHistory(Path.Combine(world.NewStorage, game.Id)), "the history did not arrive: " + world.Warnings());
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

    // Frees the hold once the settings write has given up its own read-back (it logs that it could not change the settings), so the
    // startup check's later re-read is the first read to find the file free, however slow the machine is. The deadline only frees the
    // file when that line never comes.
    private static async Task ReleaseOnceTheWriteGaveUpAsync(RecordingDiagnosticLog log, FileStream hold)
    {
        try
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
            while (DateTime.UtcNow < deadline && log.Containing("the settings could not be changed to").Count == 0)
            {
                await Task.Delay(10).ConfigureAwait(false);
            }
        }
        finally
        {
            await hold.DisposeAsync().ConfigureAwait(false);
        }
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
            WaitUntilRenamable(OldStorage);
            WaitUntilRenamable(NewStorage);
            _upgraded = true;
            Workspace.Restart();
        }

        // What the startup check logged as going wrong, so a failed assertion says why.
        public string Warnings() =>
            string.Join(" | ", Log.Entries.Where(entry => entry.Level == LogLevel.Warning).Select(entry => entry.Message));

        // Windows keeps a folder git just worked in busy for a moment after its processes have ended and refuses to rename it. The
        // startup check renames once and, by design, leaves the data where it is when that fails, so the scene waits until a rename
        // takes before the build starts. A folder that never frees fails here with the I/O error instead of as a missing move.
        private static void WaitUntilRenamable(string folder)
        {
            if (!Directory.Exists(folder))
            {
                return;
            }

            var probe = folder + ".probe";
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
            while (true)
            {
                try
                {
                    if (!Directory.Exists(probe))
                    {
                        Directory.Move(folder, probe);
                    }

                    Directory.Move(probe, folder);
                    return;
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException && DateTime.UtcNow < deadline)
                {
                    Thread.Sleep(20);
                }
            }
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

        // Writes the stored root straight to the file, as if a settings write that seemed to land never had.
        public void RevertSettingsTo(string root) =>
            Task.Run(() => WhileHeldAsync(() => Workspace.Backend.Services.Catalog.UpdateSettingsAsync(current => current! with { StorageRoot = root }))).GetAwaiter().GetResult();

        // Removes the settings file, as if a write that seemed to land had not.
        public void DeleteSettings() =>
            Task.Run(() => WhileHeldAsync(() =>
            {
                File.Delete(Path.Combine(Workspace.CatalogDirectory, CatalogStore.SettingsFileName));
                return Task.CompletedTask;
            })).GetAwaiter().GetResult();

        // The settings file can be held for a moment by a program the test does not control (a virus scanner or the search indexer
        // on Windows). The catalog's own retries give up after about a second, so a step the test depends on keeps trying until the
        // file is free instead of leaving the scene half set up. Only a held file is retried: any other failure in the step shows at once.
        private static async Task WhileHeldAsync(Func<Task> step)
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
            while (true)
            {
                try
                {
                    await step().ConfigureAwait(false);
                    return;
                }
                catch (Exception e) when (IsHeldFile(e) && DateTime.UtcNow < deadline)
                {
                    await Task.Delay(20).ConfigureAwait(false);
                }
            }
        }

        // A read of a held catalog file comes back as the catalog's corrupt error with the IO error inside; a bad payload has no IO error inside.
        private static bool IsHeldFile(Exception e) =>
            e is IOException or UnauthorizedAccessException
            || e is CatalogCorruptException { InnerException: IOException or UnauthorizedAccessException };

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
