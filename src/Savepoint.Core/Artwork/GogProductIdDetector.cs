using System.Text.Json;

namespace Savepoint.Core.Artwork;

// What the install folder's info files say. FolderReadable is false when the folder could not be listed (an unplugged drive), which says
// nothing about the game; a readable folder with no usable file really has no GOG product.
internal readonly record struct GogDetection(string? ProductId, bool FolderReadable);

// Works out a game's GOG product id from the goggame-<id>.info files GOG's installer leaves in the install folder. Reads files only.
internal static class GogProductIdDetector
{
    // Real files are about one kilobyte; anything much bigger is something else.
    private const int _infoFileLimit = 256 * 1024;

    // A product id is a short run of digits; the length cap keeps a hostile value from becoming a long folder name.
    private const int _maxIdLength = 20;

    private readonly record struct Candidate(string Id, bool IsBase);

    // The base game's id, or null when the folder holds no readable info file.
    public static string? Detect(string? installPath) => Inspect(installPath).ProductId;

    // A base game and its add-ons each leave a file, and every file names the base game as rootGameId, so the id most files name counts. In a
    // tie the one with a file of its own wins (an add-on's id has none unless the add-on is the base), then the smallest id. An older file has
    // no rootGameId and names only its own game; it is the base when it lists play tasks, which add-on files never do.
    public static GogDetection Inspect(string? installPath)
    {
        if (string.IsNullOrWhiteSpace(installPath))
        {
            return new GogDetection(null, false);
        }

        try
        {
            var options = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
            var candidates = new List<Candidate>();
            foreach (var path in Directory.EnumerateFiles(installPath, "goggame-*.info", options).Order(StringComparer.OrdinalIgnoreCase))
            {
                if (ReadCandidate(path) is { } candidate)
                {
                    candidates.Add(candidate);
                }
            }

            var id = candidates
                .GroupBy(candidate => candidate.Id, StringComparer.Ordinal)
                .OrderByDescending(group => group.Count())
                .ThenByDescending(group => group.Any(candidate => candidate.IsBase))
                .ThenBy(group => group.Key.Length)
                .ThenBy(group => group.Key, StringComparer.Ordinal)
                .Select(group => group.Key)
                .FirstOrDefault();
            return new GogDetection(id, true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return new GogDetection(null, false);
        }
    }

    private static Candidate? ReadCandidate(string path)
    {
        try
        {
            if (new FileInfo(path).Length > _infoFileLimit)
            {
                return null;
            }

            // The JSON parser refuses a byte order mark, which some installers write.
            ReadOnlyMemory<byte> bytes = File.ReadAllBytes(path);
            using var document = JsonDocument.Parse(bytes.Span.StartsWith("﻿"u8) ? bytes[3..] : bytes);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            var gameId = IdOf(root, "gameId");
            if (IdOf(root, "rootGameId") is { } rootId)
            {
                return new Candidate(rootId, gameId == rootId);
            }

            return gameId is null ? null : new Candidate(gameId, HasPlayTasks(root));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private static bool HasPlayTasks(JsonElement root) =>
        root.TryGetProperty("playTasks", out var tasks) && tasks.ValueKind == JsonValueKind.Array && tasks.GetArrayLength() > 0;

    private static string? IdOf(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            && value.GetString() is { Length: > 0 and <= _maxIdLength } id && id.All(char.IsAsciiDigit)
            ? id
            : null;
}
