using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using Savepoint.Core.Errors;

namespace Savepoint.Core.Artwork;

// What is remembered about the last lookup for a game: the Steam app it was for, and when a lookup learned an image does not exist.
internal sealed record LookupState(int? AppId, DateTimeOffset? MissAt);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(LookupState))]
internal sealed partial class ArtworkJsonContext : JsonSerializerContext
{
}

// Where a game's artwork lives on disk: <config>/artwork/<gameId>/ (D30), in SavePoint's configuration folder and never in the storage
// root. A folder holds the user's own image (custom.<ext>), the two Steam images, and the lookup memory. The methods are plain file
// work; callers hold the game's lease (EnterAsync) around anything that must not overlap a delete or another writer.
internal sealed class ArtworkStore
{
    public const int MaxCustomMegabytes = 25;
    public const long MaxCustomBytes = MaxCustomMegabytes * 1024L * 1024;

    private const string _stateFile = "lookup.json";
    private const string _customStem = "custom";

    private readonly ConcurrentDictionary<string, SemaphoreSlim> _leases = new(StringComparer.Ordinal);

    public ArtworkStore(string catalogDirectory)
    {
        ArgumentNullException.ThrowIfNull(catalogDirectory);
        Root = Path.Combine(catalogDirectory, "artwork");
    }

    public string Root { get; }

    public string FolderOf(string gameId)
    {
        // Ids are checked when a game is saved, so this only guards the one place a path is built from one.
        if (gameId.Length == 0 || gameId is "." or ".." || gameId.AsSpan().IndexOfAny('/', '\\', ':') >= 0)
        {
            throw new ArgumentException($"'{gameId}' cannot name a game's artwork folder.", nameof(gameId));
        }

        return Path.Combine(Root, gameId);
    }

    // One writer per game at a time, so a delete and a lookup finishing never interleave. Waiting is short: nothing holds a lease across the network.
    public async Task<IDisposable> EnterAsync(string gameId)
    {
        var gate = _leases.GetOrAdd(gameId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync().ConfigureAwait(false);
        return new Lease(gate);
    }

    public string? CustomPath(string gameId)
    {
        var folder = FolderOf(gameId);
        return Directory.Exists(folder)
            ? Directory.EnumerateFiles(folder, _customStem + ".*").FirstOrDefault(file => ExtensionIsImage(Path.GetExtension(file)))
            : null;
    }

    public string? SteamPath(string gameId, ArtworkKind kind)
    {
        var path = SteamFile(gameId, kind);
        return File.Exists(path) ? path : null;
    }

    public LookupState ReadState(string gameId)
    {
        try
        {
            var path = Path.Combine(FolderOf(gameId), _stateFile);
            return File.Exists(path)
                ? JsonSerializer.Deserialize(File.ReadAllText(path), ArtworkJsonContext.Default.LookupState) ?? new LookupState(null, null)
                : new LookupState(null, null);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            // A state nobody can read is the same as none: the lookup is simply made again.
            return new LookupState(null, null);
        }
    }

    public void WriteState(string gameId, LookupState state) =>
        WriteAtomically(Path.Combine(FolderOf(gameId), _stateFile), JsonSerializer.SerializeToUtf8Bytes(state, ArtworkJsonContext.Default.LookupState));

    public void SaveSteamImage(string gameId, ArtworkKind kind, byte[] bytes) =>
        WriteAtomically(SteamFile(gameId, kind), bytes);

    // True when something was removed.
    public bool DeleteSteamImages(string gameId)
    {
        var removed = false;
        foreach (var kind in new[] { ArtworkKind.Wide, ArtworkKind.Square })
        {
            removed |= DeleteFile(SteamFile(gameId, kind));
        }

        DeleteFile(Path.Combine(FolderOf(gameId), _stateFile));
        return removed;
    }

    // Copies the image in, replacing the game's own image. Nothing is created or changed unless the file is a whole image of a size allowed.
    public string SaveCustom(string gameId, string sourcePath)
    {
        var source = new FileInfo(sourcePath);
        if (!source.Exists)
        {
            throw new ArtworkNotAnImageException(sourcePath);
        }

        if (source.Length > MaxCustomBytes)
        {
            throw new ArtworkTooLargeException(sourcePath, MaxCustomMegabytes);
        }

        // A file that cannot be read at all is to the user a file that cannot be used.
        if (ImageProbe.ReadFile(sourcePath) is null)
        {
            throw new ArtworkNotAnImageException(sourcePath);
        }

        var folder = FolderOf(gameId);
        Directory.CreateDirectory(folder);
        var temporary = Path.Combine(folder, $"{_customStem}.{Guid.NewGuid():N}.tmp");
        try
        {
            CopyCapped(sourcePath, temporary);

            // The copy is what is judged, in case the file changed while it was read.
            var format = ImageProbe.ReadFile(temporary)?.Format ?? throw new ArtworkNotAnImageException(sourcePath);
            foreach (var old in Directory.EnumerateFiles(folder, _customStem + ".*").Where(file => ExtensionIsImage(Path.GetExtension(file))))
            {
                File.Delete(old);
            }

            var target = Path.Combine(folder, _customStem + ExtensionOf(format));
            File.Move(temporary, target, overwrite: true);
            return target;
        }
        finally
        {
            DeleteFile(temporary);
        }
    }

    // True when the game had an image of its own.
    public bool DeleteCustom(string gameId)
    {
        var folder = FolderOf(gameId);
        if (!Directory.Exists(folder))
        {
            return false;
        }

        var removed = false;
        foreach (var file in Directory.EnumerateFiles(folder, _customStem + ".*").Where(file => ExtensionIsImage(Path.GetExtension(file))).ToList())
        {
            removed |= DeleteFile(file);
        }

        return removed;
    }

    // Best effort: the game is already gone from the catalog, and "Clear all data" removes whatever could not be.
    public void DeleteGame(string gameId)
    {
        var folder = FolderOf(gameId);
        var gate = _leases.GetOrAdd(gameId, _ => new SemaphoreSlim(1, 1));
        gate.Wait();
        try
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // Left in place; it is small and the next "Clear all data" takes it.
        }
        finally
        {
            gate.Release();
        }
    }

    // Removes every game's artwork, including that of games the catalog no longer lists. Failures are the caller's to report.
    public void DeleteAll()
    {
        if (Directory.Exists(Root))
        {
            Directory.Delete(Root, recursive: true);
        }
    }

    private string SteamFile(string gameId, ArtworkKind kind) =>
        Path.Combine(FolderOf(gameId), kind == ArtworkKind.Wide ? "steam-wide.jpg" : "steam-square.jpg");

    private static bool ExtensionIsImage(string extension) => extension is ".png" or ".jpg" or ".webp";

    private static string ExtensionOf(ImageFormat format) => format switch
    {
        ImageFormat.Png => ".png",
        ImageFormat.Jpeg => ".jpg",
        _ => ".webp",
    };

    // Reads at most one byte more than the limit, so a file that grew while it was copied is refused, not copied whole.
    private static void CopyCapped(string sourcePath, string targetPath)
    {
        using var source = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var target = new FileStream(targetPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        var buffer = new byte[81_920];
        long total = 0;
        int read;
        while ((read = source.Read(buffer, 0, buffer.Length)) > 0)
        {
            total += read;
            if (total > MaxCustomBytes)
            {
                throw new ArtworkTooLargeException(sourcePath, MaxCustomMegabytes);
            }

            target.Write(buffer, 0, read);
        }
    }

    private static void WriteAtomically(string path, byte[] content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllBytes(temporary, content);
            File.Move(temporary, path, overwrite: true);
        }
        finally
        {
            DeleteFile(temporary);
        }
    }

    private static bool DeleteFile(string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        File.Delete(path);
        return true;
    }

    private sealed class Lease(SemaphoreSlim gate) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                gate.Release();
            }
        }
    }
}
