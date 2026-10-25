using System.Text.Json;

// Usage: PackageSources <build/msys2-sources.json>
// Prints one "<file> <url> <sha256>" line per entry of "archives", with the hash trimmed and in lowercase (empty when the entry pins
// none). build/package.sh reads the list through this, so the layout of the JSON cannot change what it reads. Exits 1 with a message
// when the list is missing, unreadable, or names no archive, and 2 on a wrong command line.
if (args.Length != 1)
{
    Console.Error.WriteLine("usage: PackageSources <msys2-sources.json>");
    return 2;
}

try
{
    using var document = JsonDocument.Parse(File.ReadAllText(args[0]));
    if (document.RootElement.ValueKind != JsonValueKind.Object
        || !document.RootElement.TryGetProperty("archives", out var archives)
        || archives.ValueKind != JsonValueKind.Array
        || archives.GetArrayLength() == 0)
    {
        Console.Error.WriteLine($"{args[0]} lists no source archives in \"archives\".");
        return 1;
    }

    var lines = new List<string>();
    foreach (var archive in archives.EnumerateArray())
    {
        var file = Text(archive, "file");
        var url = Text(archive, "url");
        if (file.Length == 0 || url.Length == 0 || file.Any(char.IsWhiteSpace) || url.Any(char.IsWhiteSpace))
        {
            Console.Error.WriteLine($"{args[0]} has an archive without a usable \"file\" and \"url\" (no spaces allowed).");
            return 1;
        }

        lines.Add($"{file} {url} {Text(archive, "sha256").ToLowerInvariant()}");
    }

    // Written as one block with plain line feeds, whatever the platform's line ending is.
    Console.Out.Write(string.Concat(lines.Select(line => line + "\n")));
    return 0;
}
catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
{
    Console.Error.WriteLine($"{args[0]} could not be read: {ex.Message}");
    return 1;
}

static string Text(JsonElement archive, string name) =>
    archive.ValueKind == JsonValueKind.Object && archive.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
        ? value.GetString()!.Trim()
        : string.Empty;
