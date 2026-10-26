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

    private GalaxyCloudReader Reader() => new(_programData.Path, _localAppData.Path, _temp.Path);

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
