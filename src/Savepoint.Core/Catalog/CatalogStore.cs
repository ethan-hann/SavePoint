using System.Buffers;
using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Savepoint.Core.Errors;

namespace Savepoint.Core.Catalog;

public sealed class CatalogStore
{
    public const string GamesFileName = "games.json";
    public const string ProfilesFileName = "profiles.json";
    public const string SettingsFileName = "settings.json";

    private const int _ioRetries = 4;
    private const int _firstRetryDelayMs = 20;

    private static readonly byte[] _utf8Bom = [0xEF, 0xBB, 0xBF];

    // Relaxed escaping keeps accented names and apostrophes readable in a hand-edited file.
    private static readonly JsonWriterOptions _writerOptions = new()
    {
        Indented = true,
        NewLine = "\n",
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public CatalogStore(string directory, TimeProvider? timeProvider = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        DirectoryPath = directory;
        _time = timeProvider ?? TimeProvider.System;
    }

    public string DirectoryPath { get; }

    public Task<IReadOnlyList<GameDefinition>> ListGamesAsync(CancellationToken cancellationToken = default) =>
        RunAsync<IReadOnlyList<GameDefinition>>(async () => await ReadGamesAsync(cancellationToken), cancellationToken);

    public Task<GameDefinition?> GetGameAsync(string id, CancellationToken cancellationToken = default) =>
        RunAsync(async () => (await ReadGamesAsync(cancellationToken)).Find(g => g.Id == id), cancellationToken);

    public async Task<GameDefinition> SaveGameAsync(GameDefinition game, CancellationToken cancellationToken = default)
    {
        var valid = CatalogValidator.RequireValid(game);
        return await RunAsync(
            async () =>
            {
                var games = await ReadGamesAsync(cancellationToken);
                Upsert(games, valid, g => g.Id);
                await WriteAsync(GamesFileName, games, CatalogJsonContext.Default.ListGameDefinition, cancellationToken);
                return valid;
            },
            cancellationToken);
    }

    public Task<bool> DeleteGameAsync(string id, CancellationToken cancellationToken = default) =>
        RunAsync(
            async () =>
            {
                var games = await ReadGamesAsync(cancellationToken);
                if (games.RemoveAll(g => g.Id == id) == 0)
                {
                    return false;
                }

                await WriteAsync(GamesFileName, games, CatalogJsonContext.Default.ListGameDefinition, cancellationToken);
                return true;
            },
            cancellationToken);

    public Task<IReadOnlyList<Profile>> ListProfilesAsync(string? gameId = null, CancellationToken cancellationToken = default) =>
        RunAsync<IReadOnlyList<Profile>>(
            async () =>
            {
                var profiles = await ReadProfilesAsync(cancellationToken);
                return gameId is null ? profiles : profiles.FindAll(p => p.GameId == gameId);
            },
            cancellationToken);

    public Task<Profile?> GetProfileAsync(string id, CancellationToken cancellationToken = default) =>
        RunAsync(async () => (await ReadProfilesAsync(cancellationToken)).Find(p => p.Id == id), cancellationToken);

    public async Task<Profile> SaveProfileAsync(Profile profile, CancellationToken cancellationToken = default)
    {
        var valid = CatalogValidator.RequireValid(profile);
        return await RunAsync(
            async () =>
            {
                var profiles = await ReadProfilesAsync(cancellationToken);
                Upsert(profiles, valid, p => p.Id);
                await WriteAsync(ProfilesFileName, profiles, CatalogJsonContext.Default.ListProfile, cancellationToken);
                return valid;
            },
            cancellationToken);
    }

    // Read-modify-write under the lock, so a launch timestamp and a rename can never overwrite each other.
    public Task<Profile?> UpdateProfileAsync(string id, Func<Profile, Profile> update, CancellationToken cancellationToken = default) =>
        RunAsync(
            async () =>
            {
                var profiles = await ReadProfilesAsync(cancellationToken);
                var index = profiles.FindIndex(p => p.Id == id);
                if (index < 0)
                {
                    return null;
                }

                var updated = CatalogValidator.RequireValid(update(profiles[index]));
                if (updated.Id != id)
                {
                    throw new InvalidOperationException("An update cannot change the id of a profile.");
                }

                profiles[index] = updated;
                await WriteAsync(ProfilesFileName, profiles, CatalogJsonContext.Default.ListProfile, cancellationToken);
                return updated;
            },
            cancellationToken);

    public Task<bool> DeleteProfileAsync(string id, CancellationToken cancellationToken = default) =>
        RunAsync(
            async () =>
            {
                var profiles = await ReadProfilesAsync(cancellationToken);
                if (profiles.RemoveAll(p => p.Id == id) == 0)
                {
                    return false;
                }

                await WriteAsync(ProfilesFileName, profiles, CatalogJsonContext.Default.ListProfile, cancellationToken);
                return true;
            },
            cancellationToken);

    public Task<AppSettings?> GetSettingsAsync(CancellationToken cancellationToken = default) =>
        RunAsync(() => ReadSettingsAsync(cancellationToken), cancellationToken);

    public Task<AppSettings> SaveSettingsAsync(AppSettings settings, CancellationToken cancellationToken = default) =>
        UpdateSettingsAsync(_ => settings, cancellationToken);

    // The current file is read even when the new value ignores it, so a corrupt file is never overwritten.
    public Task<AppSettings> UpdateSettingsAsync(Func<AppSettings?, AppSettings> update, CancellationToken cancellationToken = default) =>
        RunAsync(
            async () =>
            {
                var current = await ReadSettingsAsync(cancellationToken);
                var next = CatalogValidator.RequireValid(update(current));
                await WriteAsync(SettingsFileName, next, CatalogJsonContext.Default.AppSettings, cancellationToken);
                return next;
            },
            cancellationToken);

    // Moves a corrupt catalog file aside so the app can start empty; a readable or missing file is left alone.
    public async Task<string?> ReplaceCorruptFileAsync(string path, CancellationToken cancellationToken = default)
    {
        var fileName = ResolveCatalogFileName(path);
        return await RunAsync<string?>(
            async () =>
            {
                if (!await IsCorruptAsync(fileName, cancellationToken))
                {
                    return null;
                }

                var source = PathOf(fileName);
                var stamp = _time.GetUtcNow().ToUniversalTime().ToString("yyyyMMdd'T'HHmmssfff'Z'", CultureInfo.InvariantCulture);
                var target = FirstFreePath($"{source}.corrupt-{stamp}");
                await RetryTransientAsync(() => File.Move(source, target), cancellationToken);
                return target;
            },
            cancellationToken);
    }

    private static void Upsert<T>(List<T> items, T item, Func<T, string> idOf)
    {
        var index = items.FindIndex(existing => idOf(existing) == idOf(item));
        if (index >= 0)
        {
            items[index] = item;
        }
        else
        {
            items.Add(item);
        }
    }

    private static string FirstFreePath(string path)
    {
        var candidate = path;
        for (var n = 1; File.Exists(candidate); n++)
        {
            candidate = $"{path}-{n}";
        }

        return candidate;
    }

    // A virus scanner or indexer can hold a freshly written file for a moment; that must not look like corruption or fail a save.
    private static async Task<T> RetryTransientAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await action();
            }
            catch (Exception ex) when (attempt < _ioRetries && IsTransient(ex))
            {
                await Task.Delay(TimeSpan.FromMilliseconds(_firstRetryDelayMs << attempt), cancellationToken);
            }
        }
    }

    private static Task RetryTransientAsync(Action action, CancellationToken cancellationToken) =>
        RetryTransientAsync(
            () =>
            {
                action();
                return Task.FromResult(true);
            },
            cancellationToken);

    private static bool IsTransient(Exception ex) =>
        ex is UnauthorizedAccessException || (ex is IOException and not FileNotFoundException and not DirectoryNotFoundException);

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // A leftover that stays locked is retried by the next write.
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private string PathOf(string fileName) => Path.Combine(DirectoryPath, fileName);

    private string ResolveCatalogFileName(string path)
    {
        var fileName = Path.GetFileName(path);
        if (fileName is not (GamesFileName or ProfilesFileName or SettingsFileName))
        {
            throw new ArgumentException($"'{path}' is not a catalog file.", nameof(path));
        }

        var folder = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(folder) && !IsSameFolder(folder, DirectoryPath))
        {
            throw new ArgumentException($"'{path}' is not in this catalog's folder.", nameof(path));
        }

        return fileName;
    }

    private static bool IsSameFolder(string left, string right)
    {
        var comparison = OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        return string.Equals(
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(left)),
            Path.TrimEndingDirectorySeparator(Path.GetFullPath(right)),
            comparison);
    }

    private async Task<bool> IsCorruptAsync(string fileName, CancellationToken cancellationToken)
    {
        try
        {
            switch (fileName)
            {
                case GamesFileName:
                    await ReadGamesAsync(cancellationToken);
                    break;
                case ProfilesFileName:
                    await ReadProfilesAsync(cancellationToken);
                    break;
                default:
                    await ReadSettingsAsync(cancellationToken);
                    break;
            }

            return false;
        }
        catch (CatalogCorruptException)
        {
            return true;
        }
    }

    private Task<List<GameDefinition>> ReadGamesAsync(CancellationToken cancellationToken) =>
        ReadListAsync(GamesFileName, CatalogJsonContext.Default.ListGameDefinition, cancellationToken);

    private Task<List<Profile>> ReadProfilesAsync(CancellationToken cancellationToken) =>
        ReadListAsync(ProfilesFileName, CatalogJsonContext.Default.ListProfile, cancellationToken);

    // Missing means empty; anything unreadable is corrupt so a later write cannot silently drop the other records.
    private async Task<List<T>> ReadListAsync<T>(string fileName, JsonTypeInfo<List<T>> typeInfo, CancellationToken cancellationToken)
        where T : class
    {
        var path = PathOf(fileName);
        var bytes = await ReadBytesAsync(path, cancellationToken);
        if (bytes is null)
        {
            return [];
        }

        var list = Deserialize(path, bytes, typeInfo);
        if (list is null || list.Exists(item => item is null))
        {
            throw new CatalogCorruptException(path, null);
        }

        return list;
    }

    private async Task<AppSettings?> ReadSettingsAsync(CancellationToken cancellationToken)
    {
        var path = PathOf(SettingsFileName);
        var bytes = await ReadBytesAsync(path, cancellationToken);
        if (bytes is null)
        {
            return null;
        }

        // A literal null is valid JSON but not a settings object; treating it as "no settings" would hide corruption.
        return Deserialize(path, bytes, CatalogJsonContext.Default.AppSettings)
            ?? throw new CatalogCorruptException(path, null);
    }

    private static async Task<byte[]?> ReadBytesAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            return await RetryTransientAsync(() => File.ReadAllBytesAsync(path, cancellationToken), cancellationToken);
        }
        catch (FileNotFoundException)
        {
            return null;
        }
        catch (DirectoryNotFoundException)
        {
            return null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new CatalogCorruptException(path, ex);
        }
    }

    private static T? Deserialize<T>(string path, byte[] bytes, JsonTypeInfo<T> typeInfo)
    {
        // A hand-edited file may carry a byte order mark; the JSON reader rejects it.
        var json = bytes.AsSpan().StartsWith(_utf8Bom) ? bytes.AsSpan(_utf8Bom.Length) : bytes;
        try
        {
            return JsonSerializer.Deserialize(json, typeInfo);
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or NotSupportedException or FormatException)
        {
            throw new CatalogCorruptException(path, ex);
        }
    }

    // Written through the writer so the output is UTF-8 with LF line ends on every OS and keeps non-ASCII text readable.
    private static byte[] Serialize<T>(T value, JsonTypeInfo<T> typeInfo)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer, _writerOptions))
        {
            JsonSerializer.Serialize(writer, value, typeInfo);
        }

        buffer.Write("\n"u8);
        return buffer.WrittenSpan.ToArray();
    }

    // Temp file in the same folder, flushed to disk, then renamed over the target: a crash leaves the old file or the new one.
    private async Task WriteAsync<T>(string fileName, T value, JsonTypeInfo<T> typeInfo, CancellationToken cancellationToken)
    {
        var target = PathOf(fileName);
        var bytes = Serialize(value, typeInfo);

        Directory.CreateDirectory(DirectoryPath);
        var temp = $"{target}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.Asynchronous))
            {
                await stream.WriteAsync(bytes, cancellationToken);
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(target))
            {
                await RetryTransientAsync(() => File.Copy(target, $"{target}.bak", overwrite: true), cancellationToken);
            }

            await RetryTransientAsync(() => File.Move(temp, target, overwrite: true), cancellationToken);
        }
        catch
        {
            TryDelete(temp);
            throw;
        }

        // Anything a crashed write left behind for this file.
        foreach (var stale in Directory.EnumerateFiles(DirectoryPath, $"{fileName}.*.tmp"))
        {
            TryDelete(stale);
        }
    }

    // One lock for every catalog operation, so a read-modify-write is never interleaved with another.
    private async Task<T> RunAsync<T>(Func<Task<T>> body, CancellationToken cancellationToken)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return await body();
        }
        finally
        {
            _gate.Release();
        }
    }
}
