using System.Buffers;
using System.Globalization;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using Savepoint.Core.Errors;

namespace Savepoint.Core.Catalog;

/// <summary>
/// Reads and writes the three catalog files (<c>games.json</c>, <c>profiles.json</c>, <c>settings.json</c>) in one folder. One
/// async lock serializes every operation on the store, so a read-modify-write is never interleaved with another (D13.3).
/// </summary>
/// <remarks>
/// A missing games or profiles file reads as an empty list and a missing settings file as null. A file that cannot be read or
/// parsed throws <see cref="CatalogCorruptException"/> and is never overwritten (D13.2). Every write goes to a temp file in the
/// same folder, is flushed to disk, and is renamed over the target, after copying the previous file to <c>.bak</c>, so a crash
/// leaves the old file or the new one. A file another program holds for a moment is retried for about 1.3 seconds. This is a
/// service-level type: it throws exceptions, which the Api facade turns into <see cref="ErrorInfo"/>. Thread-safe within one
/// instance; two instances over the same folder do not coordinate.
/// </remarks>
public sealed class CatalogStore
{
    /// <summary>The file in <see cref="DirectoryPath"/> that holds every game definition, as a JSON array.</summary>
    public const string GamesFileName = "games.json";

    /// <summary>The file in <see cref="DirectoryPath"/> that holds every profile of every game, as a JSON array.</summary>
    public const string ProfilesFileName = "profiles.json";

    /// <summary>The file in <see cref="DirectoryPath"/> that holds the app settings, as one JSON object.</summary>
    public const string SettingsFileName = "settings.json";

    // Doubling from 20 ms, so about 1.3 s in all before a locked file is given up on.
    private const int _ioRetries = 6;
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

    /// <summary>Creates a store over a folder. The folder need not exist yet; the first write creates it.</summary>
    /// <param name="directory">The catalog folder.</param>
    /// <param name="timeProvider">The clock for the stamp on a corrupt file that is moved aside; defaults to the system clock.</param>
    /// <exception cref="ArgumentException"><paramref name="directory"/> is null, empty, or blank.</exception>
    public CatalogStore(string directory, TimeProvider? timeProvider = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        DirectoryPath = directory;
        _time = timeProvider ?? TimeProvider.System;
    }

    /// <summary>The folder that holds the three catalog files.</summary>
    public string DirectoryPath { get; }

    /// <summary>Lists every game in file order.</summary>
    /// <param name="cancellationToken">Abandons the wait for the lock or the read.</param>
    /// <returns>The games, or an empty list when <c>games.json</c> does not exist.</returns>
    /// <exception cref="CatalogCorruptException">The file cannot be read or is not a valid list of games.</exception>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    public Task<IReadOnlyList<GameDefinition>> ListGamesAsync(CancellationToken cancellationToken = default) =>
        RunAsync<IReadOnlyList<GameDefinition>>(async () => await ReadGamesAsync(cancellationToken).ConfigureAwait(false), cancellationToken);

    /// <summary>Finds one game by id.</summary>
    /// <param name="id">The game id; compared exactly, case included.</param>
    /// <param name="cancellationToken">Abandons the wait for the lock or the read.</param>
    /// <returns>The game, or null when none has that id.</returns>
    /// <exception cref="CatalogCorruptException">The file cannot be read or is not a valid list of games.</exception>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    public Task<GameDefinition?> GetGameAsync(string id, CancellationToken cancellationToken = default) =>
        RunAsync(async () => (await ReadGamesAsync(cancellationToken).ConfigureAwait(false)).Find(g => g.Id == id), cancellationToken);

    /// <summary>Validates a game and adds it, or replaces the game with the same id.</summary>
    /// <param name="game">The game to store.</param>
    /// <param name="cancellationToken">Abandons the wait or the write; a write cancelled before the final rename changes nothing.</param>
    /// <returns>The normalized game that was stored (trimmed text, repeats removed from the ignore patterns and mod hints).</returns>
    /// <exception cref="ValidationException">The game is invalid; nothing is read or written.</exception>
    /// <exception cref="CatalogCorruptException">The existing file cannot be read, so nothing is written.</exception>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
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
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Removes a game's record. The game's profiles and stored history are the caller's to clean up.</summary>
    /// <param name="id">The game id.</param>
    /// <param name="cancellationToken">Abandons the wait for the lock or the write.</param>
    /// <returns>True when the game was removed; false when none had that id, in which case nothing is written.</returns>
    /// <exception cref="CatalogCorruptException">The file cannot be read or is not a valid list of games.</exception>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    public Task<bool> DeleteGameAsync(string id, CancellationToken cancellationToken = default) =>
        RunAsync(
            async () =>
            {
                var games = await ReadGamesAsync(cancellationToken).ConfigureAwait(false);
                if (games.RemoveAll(g => g.Id == id) == 0)
                {
                    return false;
                }

                await WriteAsync(GamesFileName, games, CatalogJsonContext.Default.ListGameDefinition, cancellationToken).ConfigureAwait(false);
                return true;
            },
            cancellationToken);

    /// <summary>Lists profiles in file order.</summary>
    /// <param name="gameId">Only this game's profiles; null for every game's.</param>
    /// <param name="cancellationToken">Abandons the wait for the lock or the read.</param>
    /// <returns>The profiles, or an empty list when <c>profiles.json</c> does not exist or none match.</returns>
    /// <exception cref="CatalogCorruptException">The file cannot be read or is not a valid list of profiles.</exception>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    public Task<IReadOnlyList<Profile>> ListProfilesAsync(string? gameId = null, CancellationToken cancellationToken = default) =>
        RunAsync<IReadOnlyList<Profile>>(
            async () =>
            {
                var profiles = await ReadProfilesAsync(cancellationToken).ConfigureAwait(false);
                return gameId is null ? profiles : profiles.FindAll(p => p.GameId == gameId);
            },
            cancellationToken);

    /// <summary>Finds one profile by id.</summary>
    /// <param name="id">The profile id; compared exactly, case included.</param>
    /// <param name="cancellationToken">Abandons the wait for the lock or the read.</param>
    /// <returns>The profile, or null when none has that id.</returns>
    /// <exception cref="CatalogCorruptException">The file cannot be read or is not a valid list of profiles.</exception>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    public Task<Profile?> GetProfileAsync(string id, CancellationToken cancellationToken = default) =>
        RunAsync(async () => (await ReadProfilesAsync(cancellationToken).ConfigureAwait(false)).Find(p => p.Id == id), cancellationToken);

    /// <summary>Validates a profile and adds it, or replaces the profile with the same id.</summary>
    /// <param name="profile">The profile to store.</param>
    /// <param name="cancellationToken">Abandons the wait or the write; a write cancelled before the final rename changes nothing.</param>
    /// <returns>The normalized profile that was stored.</returns>
    /// <exception cref="ValidationException">The profile is invalid; nothing is read or written.</exception>
    /// <exception cref="CatalogCorruptException">The existing file cannot be read, so nothing is written.</exception>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
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
            cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Changes one profile as a read-modify-write under the lock, so a launch timestamp and a rename can never overwrite each other.
    /// </summary>
    /// <param name="id">The profile to change.</param>
    /// <param name="update">
    /// Returns the changed profile from the current one. It runs while the lock is held, so it must be quick and must not call
    /// this store, which would wait for itself forever.
    /// </param>
    /// <param name="cancellationToken">Abandons the wait or the write; a write cancelled before the final rename changes nothing.</param>
    /// <returns>The normalized profile that was stored, or null when no profile has that id (nothing is written).</returns>
    /// <exception cref="ValidationException">The changed profile is invalid; nothing is written.</exception>
    /// <exception cref="InvalidOperationException"><paramref name="update"/> changed the profile's id.</exception>
    /// <exception cref="CatalogCorruptException">The file cannot be read or is not a valid list of profiles.</exception>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    public Task<Profile?> UpdateProfileAsync(string id, Func<Profile, Profile> update, CancellationToken cancellationToken = default) =>
        RunAsync(
            async () =>
            {
                var profiles = await ReadProfilesAsync(cancellationToken).ConfigureAwait(false);
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
                await WriteAsync(ProfilesFileName, profiles, CatalogJsonContext.Default.ListProfile, cancellationToken).ConfigureAwait(false);
                return updated;
            },
            cancellationToken);

    /// <summary>Removes a profile's record. The profile's branch and history are the caller's to clean up.</summary>
    /// <param name="id">The profile id.</param>
    /// <param name="cancellationToken">Abandons the wait for the lock or the write.</param>
    /// <returns>True when the profile was removed; false when none had that id, in which case nothing is written.</returns>
    /// <exception cref="CatalogCorruptException">The file cannot be read or is not a valid list of profiles.</exception>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    public Task<bool> DeleteProfileAsync(string id, CancellationToken cancellationToken = default) =>
        RunAsync(
            async () =>
            {
                var profiles = await ReadProfilesAsync(cancellationToken).ConfigureAwait(false);
                if (profiles.RemoveAll(p => p.Id == id) == 0)
                {
                    return false;
                }

                await WriteAsync(ProfilesFileName, profiles, CatalogJsonContext.Default.ListProfile, cancellationToken).ConfigureAwait(false);
                return true;
            },
            cancellationToken);

    /// <summary>Reads the settings.</summary>
    /// <param name="cancellationToken">Abandons the wait for the lock or the read.</param>
    /// <returns>
    /// The settings, or null when <c>settings.json</c> does not exist yet (first run); callers then start from
    /// <see cref="AppSettings.Defaults"/>.
    /// </returns>
    /// <exception cref="CatalogCorruptException">The file cannot be read or does not hold a settings object.</exception>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    public Task<AppSettings?> GetSettingsAsync(CancellationToken cancellationToken = default) =>
        RunAsync(() => ReadSettingsAsync(cancellationToken), cancellationToken);

    /// <summary>Validates the settings and stores them, replacing the file's content.</summary>
    /// <param name="settings">The settings to store.</param>
    /// <param name="cancellationToken">Abandons the wait or the write; a write cancelled before the final rename changes nothing.</param>
    /// <returns>The normalized settings that were stored.</returns>
    /// <exception cref="ValidationException">The settings are invalid; nothing is written.</exception>
    /// <exception cref="CatalogCorruptException">The existing file cannot be read, so nothing is written.</exception>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    public Task<AppSettings> SaveSettingsAsync(AppSettings settings, CancellationToken cancellationToken = default) =>
        UpdateSettingsAsync(_ => settings, cancellationToken);

    /// <summary>
    /// Changes the settings as a read-modify-write under the lock. The current file is read even when the new value ignores it,
    /// so a corrupt file is never overwritten.
    /// </summary>
    /// <param name="update">
    /// Returns the new settings from the current ones, or from null when the file does not exist yet. It runs while the lock is
    /// held, so it must be quick and must not call this store, which would wait for itself forever.
    /// </param>
    /// <param name="cancellationToken">Abandons the wait or the write; a write cancelled before the final rename changes nothing.</param>
    /// <returns>The normalized settings that were stored.</returns>
    /// <exception cref="ValidationException">The new settings are invalid; nothing is written.</exception>
    /// <exception cref="CatalogCorruptException">The existing file cannot be read, so nothing is written.</exception>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    public Task<AppSettings> UpdateSettingsAsync(Func<AppSettings?, AppSettings> update, CancellationToken cancellationToken = default) =>
        RunAsync(
            async () =>
            {
                var current = await ReadSettingsAsync(cancellationToken).ConfigureAwait(false);
                var next = CatalogValidator.RequireValid(update(current));
                await WriteAsync(SettingsFileName, next, CatalogJsonContext.Default.AppSettings, cancellationToken).ConfigureAwait(false);
                return next;
            },
            cancellationToken);

    /// <summary>
    /// Moves a corrupt catalog file aside so the app can start empty. The file is renamed to its name plus <c>.corrupt-</c> and a
    /// UTC stamp (<c>yyyyMMddTHHmmssfffZ</c>), with a numeric suffix if that name is taken; it is never deleted, and a readable or
    /// missing file is left alone.
    /// </summary>
    /// <param name="path">One of the three catalog files: the full path inside <see cref="DirectoryPath"/>, or just the file name.</param>
    /// <param name="cancellationToken">Abandons the wait for the lock or the check.</param>
    /// <returns>The path the file was moved to, or null when it was missing or readable so nothing moved.</returns>
    /// <exception cref="ArgumentException"><paramref name="path"/> is not a catalog file, or is in another folder.</exception>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
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
            cancellationToken).ConfigureAwait(false);
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
                return await action().ConfigureAwait(false);
            }
            catch (Exception ex) when (attempt < _ioRetries && IsTransient(ex))
            {
                await Task.Delay(TimeSpan.FromMilliseconds(_firstRetryDelayMs << attempt), cancellationToken).ConfigureAwait(false);
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
                    await ReadGamesAsync(cancellationToken).ConfigureAwait(false);
                    break;
                case ProfilesFileName:
                    await ReadProfilesAsync(cancellationToken).ConfigureAwait(false);
                    break;
                default:
                    await ReadSettingsAsync(cancellationToken).ConfigureAwait(false);
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
        var bytes = await ReadBytesAsync(path, cancellationToken).ConfigureAwait(false);
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
        var bytes = await ReadBytesAsync(path, cancellationToken).ConfigureAwait(false);
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
            return await RetryTransientAsync(() => File.ReadAllBytesAsync(path, cancellationToken), cancellationToken).ConfigureAwait(false);
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
                await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            if (File.Exists(target))
            {
                await RetryTransientAsync(() => File.Copy(target, $"{target}.bak", overwrite: true), cancellationToken).ConfigureAwait(false);
            }

            await RetryTransientAsync(() => File.Move(temp, target, overwrite: true), cancellationToken).ConfigureAwait(false);
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
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return await body().ConfigureAwait(false);
        }
        finally
        {
            _gate.Release();
        }
    }
}
