using System.Text.Json;

namespace Savepoint.Core.Artwork;

// Works out a game's GOG product id from the goggame-<id>.info files GOG's installer leaves in the install folder. Reads files only.
internal static class GogProductIdDetector
{
    // Real files are about one kilobyte; anything much bigger is something else.
    private const int _infoFileLimit = 256 * 1024;

    // A product id is a short run of digits; the length cap keeps a hostile value from becoming a long folder name.
    private const int _maxIdLength = 20;

    // The base game's id, or null when the folder holds no readable info file. A base game and its add-ons each leave a file, and every
    // file names the base game as rootGameId, so that is what counts; an older file has no rootGameId and names only its own game.
    public static string? Detect(string? installPath)
    {
        if (string.IsNullOrWhiteSpace(installPath))
        {
            return null;
        }

        try
        {
            var options = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
            var ids = new List<string>();
            foreach (var path in Directory.EnumerateFiles(installPath, "goggame-*.info", options).Order(StringComparer.OrdinalIgnoreCase))
            {
                if (ReadProductId(path) is { } id)
                {
                    ids.Add(id);
                }
            }

            return ids.GroupBy(id => id, StringComparer.Ordinal).OrderByDescending(group => group.Count()).Select(group => group.Key).FirstOrDefault();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    private static string? ReadProductId(string path)
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
            return document.RootElement.ValueKind == JsonValueKind.Object
                ? IdOf(document.RootElement, "rootGameId") ?? IdOf(document.RootElement, "gameId")
                : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private static string? IdOf(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            && value.GetString() is { Length: > 0 and <= _maxIdLength } id && id.All(char.IsAsciiDigit)
            ? id
            : null;
}
