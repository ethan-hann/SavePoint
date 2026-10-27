using System.Text;
using System.Text.Json;

namespace Savepoint.Core.Git;

// A note in SavePoint's own runtime folder of the git processes this run has started and that are still going: who wrote it, and each git's
// id and start time. A SavePoint that is stopped from outside cannot end its gits on every system, so the next start reads the note and
// ends the ones that are still the very process it names. The folder belongs to one user, as does the single running copy of SavePoint.
internal static class RunningGitNote
{
    public const string FileName = "running-git.json";

    // Static because every registry of the process shares the one file, and a read-then-write of two of them must not interleave.
    private static readonly Lock _gate = new();

    public static string PathIn(string folder) => Path.Combine(folder, FileName);

    public static void Write(string path, ProcessStamp owner, IReadOnlyCollection<ProcessStamp> gits)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            WriteStamp(writer, "owner", owner);
            writer.WriteStartArray("gits");
            foreach (var git in gits)
            {
                WriteStamp(writer, null, git);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        AtomicText.Write(path, Encoding.UTF8.GetString(buffer.ToArray()));
    }

    // Sets what one registry (its key) has running, keeping what other registries of this process wrote to the same note. The note still holds
    // the owner and one list of every git at the top level, which is all an older start reads; each registry's own list is kept beside it so a
    // later write can replace only its own. An empty list takes the registry's part away, and the note goes when no part is left. Parts
    // written by another owner are dropped: that run is over, and the start that found them carries the ones that still run.
    public static void Update(string path, ProcessStamp owner, Guid key, IReadOnlyCollection<ProcessStamp> gits)
    {
        lock (_gate)
        {
            var parts = ReadParts(path, owner);
            parts.Remove(key);
            if (gits.Count > 0)
            {
                parts[key] = [.. gits];
            }

            if (parts.Count == 0)
            {
                Clear(path);
                return;
            }

            WriteParts(path, owner, parts);
        }
    }

    // Null when there is no note or it cannot be understood: a note that cannot be read must not hold anything up.
    public static (ProcessStamp Owner, IReadOnlyList<ProcessStamp> Gits)? Read(string path)
    {
        if (AtomicText.ReadOrNull(path) is not { } text)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(text);
            var root = document.RootElement;
            var gits = root.GetProperty("gits").EnumerateArray().Select(ReadStamp).ToList();
            return (ReadStamp(root.GetProperty("owner")), gits);
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        {
            return null;
        }
    }

    // A scanner can hold the file for a moment, so a few tries are made. No note means nothing to retry: a folder that is gone makes the
    // delete throw, and the exit hook clears the note of every backend the process built.
    public static void Clear(string path)
    {
        if (!File.Exists(path))
        {
            return;
        }

        for (var attempt = 1; attempt <= 4; attempt++)
        {
            try
            {
                File.Delete(path);
                return;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(30 * attempt);
            }
        }
    }

    // The parts of a note the given owner wrote, with the gits that ended left out; a note of any other owner, or one that cannot be read, has none.
    private static Dictionary<Guid, List<ProcessStamp>> ReadParts(string path, ProcessStamp owner)
    {
        var parts = new Dictionary<Guid, List<ProcessStamp>>();
        if (AtomicText.ReadOrNull(path) is not { } text)
        {
            return parts;
        }

        try
        {
            using var document = JsonDocument.Parse(text);
            var root = document.RootElement;
            if (ReadStamp(root.GetProperty("owner")) != owner || !root.TryGetProperty("registries", out var registries))
            {
                return parts;
            }

            foreach (var part in registries.EnumerateArray())
            {
                var running = part.GetProperty("gits").EnumerateArray().Select(ReadStamp).Where(git => git.IsRunning()).ToList();
                if (running.Count > 0)
                {
                    parts[Guid.Parse(part.GetProperty("key").GetString()!)] = running;
                }
            }
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        {
            parts.Clear();
        }

        return parts;
    }

    private static void WriteParts(string path, ProcessStamp owner, Dictionary<Guid, List<ProcessStamp>> parts)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            WriteStamp(writer, "owner", owner);
            writer.WriteStartArray("gits");
            foreach (var git in parts.Values.SelectMany(part => part).Distinct())
            {
                WriteStamp(writer, null, git);
            }

            writer.WriteEndArray();
            writer.WriteStartArray("registries");
            foreach (var (key, gits) in parts)
            {
                writer.WriteStartObject();
                writer.WriteString("key", key);
                writer.WriteStartArray("gits");
                foreach (var git in gits)
                {
                    WriteStamp(writer, null, git);
                }

                writer.WriteEndArray();
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        AtomicText.Write(path, Encoding.UTF8.GetString(buffer.ToArray()));
    }

    private static void WriteStamp(Utf8JsonWriter writer, string? name, ProcessStamp stamp)
    {
        if (name is null)
        {
            writer.WriteStartObject();
        }
        else
        {
            writer.WriteStartObject(name);
        }

        writer.WriteNumber("id", stamp.Id);
        writer.WriteNumber("startedAtUtcTicks", stamp.StartedAtUtcTicks);
        writer.WriteEndObject();
    }

    private static ProcessStamp ReadStamp(JsonElement element) =>
        new(element.GetProperty("id").GetInt32(), element.GetProperty("startedAtUtcTicks").GetInt64());
}
