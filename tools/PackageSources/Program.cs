using System.Text.Json;

// Usage: PackageSources archives <build/msys2-sources.json>
//        PackageSources tags <build/git-manifest.json>
// "archives" prints one "<file> <url> <sha256>" line per entry of "archives". "tags" prints one "<repository> <tag> <sha256>" line per
// entry of "sources", sorted by key, and insists on exactly three. The hash is trimmed and lowercase, empty when the entry pins none.
// build/package.sh reads both files through this, so the layout of the JSON cannot change what it reads. Exits 1 with a message when
// the file is missing, unreadable or does not hold what it should, and 2 on a wrong command line.
if (args.Length != 2 || args[0] is not ("archives" or "tags"))
{
    Console.Error.WriteLine("usage: PackageSources archives <msys2-sources.json> | PackageSources tags <git-manifest.json>");
    return 2;
}

var path = args[1];
try
{
    using var document = JsonDocument.Parse(File.ReadAllText(path));
    var lines = args[0] == "archives" ? Archives(document.RootElement) : Tags(document.RootElement);
    if (lines is null)
    {
        return 1;
    }

    // Written as one block with plain line feeds, whatever the platform's line ending is.
    Console.Out.Write(string.Concat(lines.Select(line => line + "\n")));
    return 0;
}
catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
{
    Console.Error.WriteLine($"{path} could not be read: {ex.Message}");
    return 1;
}

List<string>? Archives(JsonElement root)
{
    if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("archives", out var archives) || archives.ValueKind != JsonValueKind.Array || archives.GetArrayLength() == 0)
    {
        Console.Error.WriteLine($"{path} lists no source archives in \"archives\".");
        return null;
    }

    var lines = new List<string>();
    foreach (var archive in archives.EnumerateArray())
    {
        var file = Text(archive, "file");
        var url = Text(archive, "url");
        if (!Usable(file) || !Usable(url))
        {
            Console.Error.WriteLine($"{path} has an archive without a usable \"file\" and \"url\" (no spaces allowed).");
            return null;
        }

        lines.Add($"{file} {url} {Text(archive, "sha256").ToLowerInvariant()}");
    }

    return lines;
}

List<string>? Tags(JsonElement root)
{
    if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("sources", out var sources) || sources.ValueKind != JsonValueKind.Object)
    {
        Console.Error.WriteLine($"{path} pins no source archives in \"sources\".");
        return null;
    }

    var entries = sources.EnumerateObject().OrderBy(entry => entry.Name, StringComparer.Ordinal).ToList();
    if (entries.Count != 3)
    {
        Console.Error.WriteLine($"{path} has {entries.Count} source entries in \"sources\"; expected 3 (git, Git for Windows, dugite-native).");
        return null;
    }

    var lines = new List<string>();
    foreach (var entry in entries)
    {
        var repository = Text(entry.Value, "repository");
        var tag = Text(entry.Value, "tag");
        if (!Usable(repository) || !Usable(tag))
        {
            Console.Error.WriteLine($"{path} has a source entry without a usable \"repository\" and \"tag\" (no spaces allowed): {entry.Name}.");
            return null;
        }

        lines.Add($"{repository} {tag} {Text(entry.Value, "sha256").ToLowerInvariant()}");
    }

    return lines;
}

static bool Usable(string value) => value.Length > 0 && !value.Any(char.IsWhiteSpace);

static string Text(JsonElement entry, string name) =>
    entry.ValueKind == JsonValueKind.Object && entry.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
        ? value.GetString()!.Trim()
        : string.Empty;
