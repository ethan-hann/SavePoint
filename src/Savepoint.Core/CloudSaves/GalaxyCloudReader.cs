using System.Text.Json;
using Microsoft.Data.Sqlite;
using Savepoint.Core.Api;

namespace Savepoint.Core.CloudSaves;

/// <summary>
/// Reads GOG Galaxy's cloud-save settings: the global switch in <c>config.json</c> and the per-game flag in <c>galaxy-2.0.db</c>. Galaxy
/// keeps its database open, so the database (with its <c>-wal</c> and <c>-shm</c> files) is copied to a private folder, the copy is
/// opened read-only and deleted, and the live files are only ever read.
/// </summary>
internal sealed class GalaxyCloudReader(string? programData, string? localAppData, string tempRoot) : IStoreCloudReader
{
    private const string _databaseName = "galaxy-2.0.db";
    private const long _configLimit = 1024 * 1024;

    public CloudSaveState Read(string storeGameId)
    {
        if (storeGameId.Length == 0 || storeGameId.Length > 20 || !storeGameId.All(char.IsAsciiDigit))
        {
            return CloudSaveState.Unknown;
        }

        try
        {
            if (GlobalSwitchIsOff())
            {
                return CloudSaveState.Off;
            }

            if (programData is null)
            {
                return CloudSaveState.Unknown;
            }

            var database = Path.Combine(programData, "GOG.com", "Galaxy", "storage", _databaseName);
            return File.Exists(database) ? ReadFromCopy(database, storeGameId) : CloudSaveState.Unknown;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or SqliteException)
        {
            return CloudSaveState.Unknown;
        }
    }

    // A file that is missing or cannot be read does not say the switch is off, so the game's own flag decides.
    private bool GlobalSwitchIsOff()
    {
        if (localAppData is null)
        {
            return false;
        }

        try
        {
            var file = new FileInfo(Path.Combine(localAppData, "GOG.com", "Galaxy", "Configuration", "config.json"));
            if (!file.Exists || file.Length > _configLimit)
            {
                return false;
            }

            using var document = JsonDocument.Parse(File.ReadAllBytes(file.FullName), new JsonDocumentOptions { AllowTrailingCommas = true });
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("featureCloudSaves", out var feature)
                && feature.ValueKind == JsonValueKind.False;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return false;
        }
    }

    private CloudSaveState ReadFromCopy(string database, string productId)
    {
        var folder = Path.Combine(tempRoot, "cloud-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var copy = Path.Combine(folder, _databaseName);
            foreach (var suffix in new[] { string.Empty, "-wal", "-shm" })
            {
                if (File.Exists(database + suffix))
                {
                    CopyShared(database + suffix, copy + suffix);
                }
            }

            return Query(copy, productId);
        }
        finally
        {
            DeleteFolder(folder);
        }
    }

    // Galaxy has the files open; share-friendly reading lets the copy succeed while it runs.
    private static void CopyShared(string from, string to)
    {
        using var source = new FileStream(from, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var target = new FileStream(to, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        source.CopyTo(target);
    }

    private static CloudSaveState Query(string copy, string productId)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = copy,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT cloudStorage FROM ProductSettings WHERE gameReleaseKey = $key";
        command.Parameters.AddWithValue("$key", "gog_" + productId);
        using var reader = command.ExecuteReader();

        // A game Galaxy keeps no settings row for has cloud saves off.
        if (!reader.Read())
        {
            return CloudSaveState.Off;
        }

        if (reader.IsDBNull(0))
        {
            return CloudSaveState.Unknown;
        }

        return reader.GetInt64(0) switch
        {
            1 => CloudSaveState.On,
            0 => CloudSaveState.Off,
            _ => CloudSaveState.Unknown,
        };
    }

    private static void DeleteFolder(string folder)
    {
        try
        {
            Directory.Delete(folder, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A copy that cannot be removed now is left in the temp folder; it holds only settings.
        }
    }
}
