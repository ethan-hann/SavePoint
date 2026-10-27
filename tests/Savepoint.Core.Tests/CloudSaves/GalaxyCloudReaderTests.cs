using Microsoft.Data.Sqlite;
using Savepoint.Core.Api;
using Savepoint.Core.CloudSaves;
using Savepoint.Core.Tests.Services;

namespace Savepoint.Core.Tests.CloudSaves;

// The database has the shape of Galaxy's galaxy-2.0.db: a ProductSettings table keyed by gameReleaseKey ("gog_<product id>") with a
// cloudStorage flag among other columns. config.json is Galaxy's settings file, with featureCloudSaves as the global switch.
public class GalaxyCloudReaderTests : IDisposable
{
    private readonly TempFolder _programData = new();
    private readonly TempFolder _localAppData = new();
    private readonly TempFolder _temp = new();

    private string DbPath => _programData.Combine("GOG.com", "Galaxy", "storage", "galaxy-2.0.db");

    public void Dispose()
    {
        _programData.Dispose();
        _localAppData.Dispose();
        _temp.Dispose();
    }

    private GalaxyCloudReader Reader(Action<string, int>? duringCopy = null) => new(_programData.Path, _localAppData.Path, _temp.Path, duringCopy);

    private static SqliteConnection Open(string path, bool wal)
    {
        var connection = new SqliteConnection($"Data Source={path};Pooling=False");
        connection.Open();
        if (wal)
        {
            Run(connection, "PRAGMA journal_mode=WAL");
        }

        return connection;
    }

    private static void Run(SqliteConnection connection, string sql)
    {
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    private void CreateDb(bool wal, params (string Key, int? Cloud)[] rows) => FillDb(Open(PrepareDbFolder(), wal), rows, keepOpen: false);

    private string PrepareDbFolder()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(DbPath)!);
        return DbPath;
    }

    private static SqliteConnection FillDb(SqliteConnection connection, (string Key, int? Cloud)[] rows, bool keepOpen)
    {
        Run(connection, "CREATE TABLE ProductSettings ('gameReleaseKey' TEXT NOT NULL PRIMARY KEY, 'selectedBuildId' BIGINT, 'branch' TEXT, 'autoUpdate' BOOLEAN NOT NULL, 'cloudStorage' BOOLEAN NOT NULL, 'overlay' BOOLEAN NOT NULL DEFAULT 1)");
        foreach (var (key, cloud) in rows)
        {
            using var insert = connection.CreateCommand();
            insert.CommandText = "INSERT INTO ProductSettings (gameReleaseKey, autoUpdate, cloudStorage) VALUES ($key, 1, $cloud)";
            insert.Parameters.AddWithValue("$key", key);
            insert.Parameters.AddWithValue("$cloud", cloud is { } value ? value : DBNull.Value);
            insert.ExecuteNonQuery();
        }

        if (!keepOpen)
        {
            connection.Dispose();
        }

        return connection;
    }

    private void WriteConfig(string json) => _localAppData.WriteText("GOG.com/Galaxy/Configuration/config.json", json);

    [Fact]
    public void A_row_with_cloud_storage_1_is_on()
    {
        CreateDb(false, ("gog_1207658924", 1), ("gog_1207659000", 0));

        Assert.Equal(CloudSaveState.On, Reader().Read("1207658924"));
    }

    [Fact]
    public void A_row_with_cloud_storage_0_is_off()
    {
        CreateDb(false, ("gog_1207658924", 1), ("gog_1207659000", 0));

        Assert.Equal(CloudSaveState.Off, Reader().Read("1207659000"));
    }

    [Fact]
    public void A_game_with_no_row_is_off()
    {
        CreateDb(false, ("gog_1207658924", 1));

        Assert.Equal(CloudSaveState.Off, Reader().Read("1111111111"));
    }

    [Fact]
    public void A_flag_that_is_neither_0_nor_1_is_unknown()
    {
        CreateDb(false, ("gog_1207658924", 2));

        Assert.Equal(CloudSaveState.Unknown, Reader().Read("1207658924"));
    }

    [Fact]
    public void No_database_means_galaxy_is_not_installed_and_is_unknown()
    {
        Assert.Equal(CloudSaveState.Unknown, Reader().Read("1207658924"));
    }

    [Fact]
    public void A_file_that_is_not_a_database_is_unknown()
    {
        PrepareDbFolder();
        File.WriteAllText(DbPath, "this is not a database, only text that is long enough to look like a damaged file");

        Assert.Equal(CloudSaveState.Unknown, Reader().Read("1207658924"));
    }

    [Fact]
    public void A_database_without_the_table_is_unknown()
    {
        using (var connection = Open(PrepareDbFolder(), wal: false))
        {
            Run(connection, "CREATE TABLE Other (x INTEGER)");
        }

        Assert.Equal(CloudSaveState.Unknown, Reader().Read("1207658924"));
    }

    [Fact]
    public void The_global_switch_off_makes_every_game_off_without_the_database()
    {
        WriteConfig("{ \"featureAchievements\": true, \"featureCloudSaves\": false }");

        Assert.Equal(CloudSaveState.Off, Reader().Read("1207658924"));
    }

    [Fact]
    public void The_global_switch_on_leaves_the_game_setting_in_charge()
    {
        CreateDb(false, ("gog_1207658924", 0));
        WriteConfig("{ \"featureCloudSaves\": true }");

        Assert.Equal(CloudSaveState.Off, Reader().Read("1207658924"));
    }

    [Fact]
    public void A_config_that_cannot_be_read_does_not_hide_the_game_setting()
    {
        CreateDb(false, ("gog_1207658924", 1));
        WriteConfig("{ not json");

        Assert.Equal(CloudSaveState.On, Reader().Read("1207658924"));
    }

    [Fact]
    public void Rows_still_in_the_write_ahead_log_are_seen_and_the_live_files_are_untouched()
    {
        using var live = Open(PrepareDbFolder(), wal: true);
        FillDb(live, [("gog_1207658924", 1)], keepOpen: true);
        var before = Snapshot(Path.GetDirectoryName(DbPath)!);
        Assert.Contains(before, line => line.Contains("-wal", StringComparison.Ordinal));

        var state = Reader().Read("1207658924");

        Assert.Equal(CloudSaveState.On, state);
        Assert.Equal(before, Snapshot(Path.GetDirectoryName(DbPath)!));
        Assert.Empty(Directory.EnumerateFileSystemEntries(_temp.Path, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public void The_shared_memory_file_is_not_copied()
    {
        using var live = Open(PrepareDbFolder(), wal: true);
        FillDb(live, [("gog_1207658924", 1)], keepOpen: true);
        Assert.True(File.Exists(DbPath + "-shm"));
        var copied = new List<string>();

        Reader((folder, _) => copied.AddRange(Directory.EnumerateFiles(folder).Select(Path.GetFileName).OfType<string>())).Read("1207658924");

        Assert.Contains("galaxy-2.0.db-wal", copied);
        Assert.DoesNotContain("galaxy-2.0.db-shm", copied);
    }

    [Fact]
    public void A_live_file_that_changes_during_the_copy_is_copied_again_once()
    {
        CreateDb(false, ("gog_1207658924", 1));
        var reader = Reader((_, attempt) =>
        {
            if (attempt == 0)
            {
                File.AppendAllText(DbPath, "\0");
            }
        });

        Assert.Equal(CloudSaveState.On, reader.Read("1207658924"));
        Assert.Equal(2, reader.CopiesMade);
    }

    [Fact]
    public void A_live_file_that_keeps_changing_during_the_copy_is_unknown()
    {
        CreateDb(false, ("gog_1207658924", 1));
        var reader = Reader((_, _) => File.AppendAllText(DbPath, "\0"));

        Assert.Equal(CloudSaveState.Unknown, reader.Read("1207658924"));
        Assert.Equal(2, reader.CopiesMade);
        Assert.Empty(Directory.EnumerateFileSystemEntries(_temp.Path, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public void A_moved_change_counter_is_noticed_even_when_size_and_time_stay_the_same()
    {
        CreateDb(false, ("gog_1207658924", 1));
        var reader = Reader((_, _) =>
        {
            var stamp = File.GetLastWriteTimeUtc(DbPath);
            using (var stream = new FileStream(DbPath, FileMode.Open, FileAccess.Write, FileShare.ReadWrite))
            {
                stream.Position = 27;
                var last = new byte[1];
                using var read = new FileStream(DbPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                read.Position = 27;
                _ = read.Read(last, 0, 1);
                stream.WriteByte((byte)(last[0] + 1));
            }

            File.SetLastWriteTimeUtc(DbPath, stamp);
        });

        Assert.Equal(CloudSaveState.Unknown, reader.Read("1207658924"));
    }

    [Fact]
    public void An_unchanged_database_is_copied_once_for_every_game_asked_about()
    {
        CreateDb(false, ("gog_1207658924", 1), ("gog_1207659000", 0));
        var reader = Reader();

        Assert.Equal(CloudSaveState.On, reader.Read("1207658924"));
        Assert.Equal(CloudSaveState.Off, reader.Read("1207659000"));
        Assert.Equal(CloudSaveState.On, reader.Read("1207658924"));
        Assert.Equal(1, reader.CopiesMade);
    }

    [Fact]
    public void A_changed_database_is_read_again()
    {
        CreateDb(false, ("gog_1207658924", 1));
        var reader = Reader();
        Assert.Equal(CloudSaveState.On, reader.Read("1207658924"));

        using (var live = Open(DbPath, wal: false))
        {
            Run(live, "UPDATE ProductSettings SET cloudStorage = 0");
        }

        Assert.Equal(CloudSaveState.Off, reader.Read("1207658924"));
        Assert.Equal(2, reader.CopiesMade);
    }

    [Fact]
    public void A_change_that_only_reached_the_write_ahead_log_is_read_again()
    {
        using var live = Open(PrepareDbFolder(), wal: true);
        FillDb(live, [("gog_1207658924", 1)], keepOpen: true);
        var reader = Reader();
        Assert.Equal(CloudSaveState.On, reader.Read("1207658924"));

        Run(live, "UPDATE ProductSettings SET cloudStorage = 0");

        Assert.Equal(CloudSaveState.Off, reader.Read("1207658924"));
        Assert.Equal(2, reader.CopiesMade);
    }

    [Fact]
    public void A_flag_stored_as_text_is_unknown_and_does_not_throw()
    {
        using (var connection = Open(PrepareDbFolder(), wal: false))
        {
            Run(connection, "CREATE TABLE ProductSettings ('gameReleaseKey' TEXT NOT NULL PRIMARY KEY, 'cloudStorage' BOOLEAN NOT NULL)");
            Run(connection, "INSERT INTO ProductSettings VALUES ('gog_1207658924', 'abc')");
        }

        Assert.Equal(CloudSaveState.Unknown, Reader().Read("1207658924"));
    }

    [Fact]
    public void Old_copies_left_by_a_crash_are_removed_and_recent_ones_and_other_folders_are_kept()
    {
        CreateDb(false, ("gog_1207658924", 1));
        var stale = Directory.CreateDirectory(_temp.Combine("cloud-stale")).FullName;
        File.WriteAllText(Path.Combine(stale, "galaxy-2.0.db"), "x");
        Directory.SetLastWriteTimeUtc(stale, DateTime.UtcNow.AddHours(-2));
        var recent = Directory.CreateDirectory(_temp.Combine("cloud-recent")).FullName;
        var other = Directory.CreateDirectory(_temp.Combine("not-ours")).FullName;
        Directory.SetLastWriteTimeUtc(other, DateTime.UtcNow.AddHours(-2));

        Reader().Read("1207658924");

        Assert.False(Directory.Exists(stale));
        Assert.True(Directory.Exists(recent));
        Assert.True(Directory.Exists(other));
    }

    [Fact]
    public void A_copy_becomes_a_leftover_when_the_injected_clock_moves_past_its_age()
    {
        CreateDb(false, ("gog_1207658924", 1));
        var time = new ManualTimeProvider();
        var copy = Directory.CreateDirectory(_temp.Combine("cloud-from-another-run")).FullName;
        Directory.SetLastWriteTimeUtc(copy, time.GetUtcNow().UtcDateTime.AddMinutes(-1));
        var reader = new GalaxyCloudReader(_programData.Path, _localAppData.Path, _temp.Path, timeProvider: time);

        reader.Read("1207658924");
        var keptWhileRecent = Directory.Exists(copy);
        time.Advance(TimeSpan.FromMinutes(30));
        reader.Read("1207658924");

        Assert.True(keptWhileRecent);
        Assert.False(Directory.Exists(copy));
    }

    [Fact]
    public void The_copy_is_deleted_after_a_read()
    {
        CreateDb(false, ("gog_1207658924", 1));

        Reader().Read("1207658924");

        Assert.Empty(Directory.EnumerateFileSystemEntries(_temp.Path, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public void The_copy_is_deleted_when_the_read_fails()
    {
        PrepareDbFolder();
        File.WriteAllText(DbPath, "this is not a database, only text that is long enough to look like a damaged file");

        Reader().Read("1207658924");

        Assert.Empty(Directory.EnumerateFileSystemEntries(_temp.Path, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public void An_id_that_is_not_digits_is_unknown()
    {
        CreateDb(false, ("gog_1", 1));

        Assert.Equal(CloudSaveState.Unknown, Reader().Read("1' OR '1'='1"));
    }

    [Fact]
    public void Missing_data_folders_are_unknown()
    {
        Assert.Equal(CloudSaveState.Unknown, new GalaxyCloudReader(null, null, _temp.Path).Read("1207658924"));
    }

    private static string[] Snapshot(string folder) =>
        [.. Directory.EnumerateFiles(folder)
            .Order(StringComparer.Ordinal)
            .Select(path => $"{path}|{File.GetLastWriteTimeUtc(path).Ticks}|{Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(ReadShared(path)))}")];

    private static byte[] ReadShared(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        return memory.ToArray();
    }
}
