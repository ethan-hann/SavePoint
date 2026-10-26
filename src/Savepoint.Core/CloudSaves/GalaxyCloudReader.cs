using System.Text.Json;
using Microsoft.Data.Sqlite;
using Savepoint.Core.Api;

namespace Savepoint.Core.CloudSaves;

/// <summary>
/// Reads GOG Galaxy's cloud-save settings: the global switch in <c>config.json</c> and the per-game flag in <c>galaxy-2.0.db</c>. Galaxy
/// keeps its database open, so the database and its <c>-wal</c> file are copied to a private folder, the copy is opened read-only and
/// deleted, and the live files are only ever read. The copy is trusted only if the live files did not change while it was made. The
/// flags of all games are kept for the session and reused while the live files keep the same size and time.
/// </summary>
/// <param name="programData">The machine-wide application data folder (<c>C:\ProgramData</c>); null when there is none.</param>
/// <param name="localAppData">The local application data folder; null when there is none.</param>
/// <param name="tempRoot">A folder SavePoint owns, where copies are made. Copies left behind by a crash are removed from it.</param>
/// <param name="duringCopy">Called after each copy is made, with the copy's folder and the attempt number; tests use it to change the live files at that moment.</param>
internal sealed class GalaxyCloudReader(string? programData, string? localAppData, string tempRoot, Action<string, int>? duringCopy = null) : IStoreCloudReader
{
    private const string _databaseName = "galaxy-2.0.db";
    private const long _configLimit = 1024 * 1024;

    // A crashed run leaves a copy; one this old cannot belong to a copy still being made, here or in another running SavePoint.
    private static readonly TimeSpan _leftoverAge = TimeSpan.FromMinutes(10);

    private readonly object _gate = new();
    private (Fingerprint Files, Dictionary<string, CloudSaveState> Flags)? _cache;

    /// <summary>How many times the database was copied; a read answered from the session's memory makes none.</summary>
    public int CopiesMade { get; private set; }

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
            if (!File.Exists(database))
            {
                return CloudSaveState.Unknown;
            }

            Dictionary<string, CloudSaveState>? flags;
            lock (_gate)
            {
                flags = Snapshot(database);
            }

            // A fresh install creates a game's row with cloud on, so no row means Galaxy does not manage this game's saves.
            return flags is null ? CloudSaveState.Unknown : flags.GetValueOrDefault("gog_" + storeGameId, CloudSaveState.Off);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException or SqliteException
            or InvalidCastException or DllNotFoundException or TypeInitializationException or BadImageFormatException or InvalidOperationException)
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
            if (SharedFile.ReadText(Path.Combine(localAppData, "GOG.com", "Galaxy", "Configuration", "config.json"), _configLimit) is not { } text)
            {
                return false;
            }

            using var document = JsonDocument.Parse(text, new JsonDocumentOptions { AllowTrailingCommas = true });
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("featureCloudSaves", out var feature)
                && feature.ValueKind == JsonValueKind.False;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return false;
        }
    }

    // Called with the gate held. Null when the live files would not hold still long enough to copy.
    private Dictionary<string, CloudSaveState>? Snapshot(string database)
    {
        SweepLeftovers();
        for (var attempt = 0; attempt < 2; attempt++)
        {
            var before = Fingerprint.Of(database);
            if (attempt == 0 && _cache is { } known && known.Files.SameFiles(before))
            {
                return known.Flags;
            }

            var folder = Path.Combine(tempRoot, "cloud-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            try
            {
                var copy = Path.Combine(folder, _databaseName);
                CopyShared(database, copy);
                if (File.Exists(database + "-wal"))
                {
                    CopyShared(database + "-wal", copy + "-wal");
                }

                CopiesMade++;
                duringCopy?.Invoke(folder, attempt);

                // Size and time can match after a write; the header's change counter cannot.
                if (Fingerprint.Of(database) != before)
                {
                    continue;
                }

                var flags = Query(copy);
                _cache = (before, flags);
                return flags;
            }
            finally
            {
                DeleteFolder(folder);
            }
        }

        return null;
    }

    // Galaxy has the files open; share-friendly reading lets the copy succeed while it runs.
    private static void CopyShared(string from, string to)
    {
        using var source = SharedFile.OpenRead(from);
        using var target = new FileStream(to, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        source.CopyTo(target);
    }

    private static Dictionary<string, CloudSaveState> Query(string copy)
    {
        using var connection = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = copy,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        }.ToString());
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT gameReleaseKey, cloudStorage FROM ProductSettings WHERE gameReleaseKey LIKE 'gog\\_%' ESCAPE '\\'";
        using var reader = command.ExecuteReader();
        var flags = new Dictionary<string, CloudSaveState>(StringComparer.Ordinal);
        while (reader.Read())
        {
            if (reader.GetValue(0) is string key)
            {
                flags[key] = reader.GetValue(1) switch
                {
                    1L => CloudSaveState.On,
                    0L => CloudSaveState.Off,
                    _ => CloudSaveState.Unknown,
                };
            }
        }

        return flags;
    }

    private void SweepLeftovers()
    {
        try
        {
            if (!Directory.Exists(tempRoot))
            {
                return;
            }

            foreach (var folder in Directory.EnumerateDirectories(tempRoot, "cloud-*"))
            {
                if (DateTime.UtcNow - Directory.GetLastWriteTimeUtc(folder) > _leftoverAge)
                {
                    DeleteFolder(folder);
                }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A folder that cannot be listed now is looked at on a later read.
        }
    }

    private static void DeleteFolder(string folder)
    {
        try
        {
            Directory.Delete(folder, recursive: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A copy that cannot be removed now is Galaxy's whole database; the next read sweeps it once it is old enough.
        }
    }

    // What identifies the live files at one moment: size and time of the database and its log, and the database header's change counter.
    private readonly record struct Fingerprint(long DbLength, long DbTicks, long WalLength, long WalTicks, uint Counter)
    {
        public static Fingerprint Of(string database)
        {
            var db = new FileInfo(database);
            var wal = new FileInfo(database + "-wal");
            var counter = new byte[4];
            using (var stream = SharedFile.OpenRead(database))
            {
                stream.Position = 24;
                stream.ReadExactly(counter);
            }

            return new Fingerprint(
                db.Length,
                db.LastWriteTimeUtc.Ticks,
                wal.Exists ? wal.Length : 0,
                wal.Exists ? wal.LastWriteTimeUtc.Ticks : 0,
                System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(counter));
        }

        public bool SameFiles(Fingerprint other) =>
            DbLength == other.DbLength && DbTicks == other.DbTicks && WalLength == other.WalLength && WalTicks == other.WalTicks;
    }
}
