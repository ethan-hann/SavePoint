using System.Text.Json;
using Savepoint.Core.Api;
using Savepoint.Core.Catalog;

namespace Savepoint.Core.Library;

// Turns GOG's install records into the games that are installed. GOG writes a record for every add-on too, with the same folder as
// the game, so add-ons, records with a missing folder, and a second record for one folder are left out.
internal static class GogInstalledGames
{
    private const int _infoFileLimit = 1 << 20;

    /// <summary>The installed games among <paramref name="records"/>, each folder once, sorted by name.</summary>
    /// <param name="records">What GOG recorded; nothing here is trusted to be well formed.</param>
    /// <param name="cancellationToken">Checked before each record.</param>
    public static IReadOnlyList<InstalledGame> From(IReadOnlyList<GogInstallRecord> records, CancellationToken cancellationToken = default)
    {
        var found = new List<InstalledGame>();
        var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var record in records)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(record.Id)
                || string.IsNullOrWhiteSpace(record.Name)
                || string.IsNullOrWhiteSpace(record.Path)
                || !string.IsNullOrWhiteSpace(record.DependsOn)
                || !Path.IsPathFullyQualified(record.Path.Trim())
                || !Directory.Exists(record.Path.Trim())
                || IsAddOn(record))
            {
                continue;
            }

            var folder = Path.TrimEndingDirectorySeparator(record.Path.Trim());
            if (folders.Add(folder))
            {
                var id = record.Id.Trim();
                found.Add(new InstalledGame(LibrarySource.Gog, id, record.Name.Trim(), folder, Path.GetFileName(folder), LaunchFor(record, id, folder), null));
            }
        }

        return [.. found.OrderBy(game => game.Name, StringComparer.OrdinalIgnoreCase).ThenBy(game => game.StoreId, StringComparer.Ordinal)];
    }

    // The info file GOG leaves in the game's folder names the game an add-on belongs to, for the add-ons whose record does not.
    private static bool IsAddOn(GogInstallRecord record)
    {
        try
        {
            var info = new FileInfo(Path.Combine(record.Path.Trim(), $"goggame-{record.Id.Trim()}.info"));
            if (!info.Exists || info.Length > _infoFileLimit)
            {
                return false;
            }

            using var document = JsonDocument.Parse(File.ReadAllText(info.FullName));
            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("rootGameId", out var root)
                && root.ValueKind == JsonValueKind.String
                && root.GetString() is { Length: > 0 } rootId
                && !string.Equals(rootId, record.Id.Trim(), StringComparison.Ordinal);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            return false;
        }
    }

    // The recorded program when it can be told apart inside the install folder; Galaxy's page for the game otherwise.
    private static LaunchConfig LaunchFor(GogInstallRecord record, string id, string folder)
    {
        if (!string.IsNullOrWhiteSpace(record.Exe))
        {
            var relative = Path.GetRelativePath(folder, record.Exe.Trim());
            if (!Path.IsPathRooted(relative) && !relative.StartsWith("..", StringComparison.Ordinal) && File.Exists(Path.Combine(folder, relative)))
            {
                return new LaunchConfig(LaunchMethod.Exe, relative);
            }
        }

        if (!string.IsNullOrWhiteSpace(record.ExeFile))
        {
            var name = record.ExeFile.Trim();
            if (name.IndexOfAny(['/', '\\']) < 0 && File.Exists(Path.Combine(folder, name)))
            {
                return new LaunchConfig(LaunchMethod.Exe, name);
            }
        }

        return new LaunchConfig(LaunchMethod.Uri, Uri: $"goggalaxy://openGameView/{id}");
    }
}
