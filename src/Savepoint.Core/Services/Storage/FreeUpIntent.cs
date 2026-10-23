using System.Text;
using System.Text.Json;
using Savepoint.Core.Backend;
using Savepoint.Core.Catalog;
using Savepoint.Core.Git;

namespace Savepoint.Core.Services.Storage;

// A note in a game's storage folder, written before a free-up deletes its first record and removed after the last: every ref and value it
// is about to delete in both folders. The folders are two repositories and a ref transaction covers one, so a run that is cut off between
// them would leave half a snapshot behind. The note is what lets the next start finish the clear (the player confirmed it), and until it
// has, the listing treats what the note names as gone.
internal static class FreeUpIntent
{
    public const string FileName = "savepoint-free-up.json";

    private const string _install = "install";
    private const string _external = "external";

    public static string PathIn(string gameDirectory) => Path.Combine(gameDirectory, FileName);

    public static void Write(string gameDirectory, IReadOnlyDictionary<RepoRole, IReadOnlyList<ClearedRef>> refs)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            foreach (var (role, list) in refs.OrderBy(pair => pair.Key))
            {
                writer.WriteStartArray(role == RepoRole.Install ? _install : _external);
                foreach (var item in list)
                {
                    writer.WriteStartObject();
                    writer.WriteString("name", item.RefName);
                    writer.WriteString("value", item.Value);
                    writer.WriteEndObject();
                }

                writer.WriteEndArray();
            }

            writer.WriteEndObject();
        }

        AtomicText.Write(PathIn(gameDirectory), Encoding.UTF8.GetString(buffer.ToArray()));
    }

    // Null when there is no note or it cannot be understood: a note that cannot be read names nothing to finish.
    public static IReadOnlyDictionary<RepoRole, IReadOnlyList<ClearedRef>>? Read(string gameDirectory)
    {
        if (AtomicText.ReadOrNull(PathIn(gameDirectory)) is not { } text)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(text);
            var refs = new Dictionary<RepoRole, IReadOnlyList<ClearedRef>>();
            foreach (var (name, role) in new[] { (_install, RepoRole.Install), (_external, RepoRole.External) })
            {
                if (document.RootElement.TryGetProperty(name, out var array))
                {
                    refs[role] = [.. array.EnumerateArray().Select(item => new ClearedRef(item.GetProperty("name").GetString()!, item.GetProperty("value").GetString()!))];
                }
            }

            return refs;
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or KeyNotFoundException or ArgumentNullException)
        {
            return null;
        }
    }

    // The names of the refs a pending clear is about to delete, which no listing may show.
    public static IReadOnlySet<string> NamedRefs(string gameDirectory) =>
        Read(gameDirectory) is { } pending ? pending.Values.SelectMany(list => list.Select(item => item.RefName)).ToHashSet(StringComparer.Ordinal) : new HashSet<string>();

    // A scanner can hold the file for a moment, so a few tries are made; a note that stays is finished again by the next start, which is harmless.
    public static void Clear(string gameDirectory)
    {
        for (var attempt = 1; attempt <= 4; attempt++)
        {
            try
            {
                File.Delete(PathIn(gameDirectory));
                return;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(30 * attempt);
            }
        }
    }

    // Deletes whatever a note still names, in each folder, and then removes the note. A ref already gone is skipped, so finishing a clear
    // that had finished (or half finished) is the same as finishing it once. A failure leaves the note for the next try.
    public static async Task FinishAsync(StorageSnapshot storage, string gameId, CancellationToken ct)
    {
        var gameDirectory = storage.GameDirectory(gameId);
        if (!File.Exists(PathIn(gameDirectory)))
        {
            return;
        }

        if (Read(gameDirectory) is { } pending)
        {
            foreach (var (role, refs) in pending.OrderBy(pair => pair.Key))
            {
                var repo = GameFolders.Repo(storage, gameId, new GameFolder(role, string.Empty, null, string.Empty));
                await storage.Engine.ClearExactAsync(repo, refs, ct).ConfigureAwait(false);
            }
        }

        Clear(gameDirectory);
    }
}
