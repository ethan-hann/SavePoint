using System.Text;
using System.Text.Json;

namespace Savepoint.Core.Git;

// A note in SavePoint's own runtime folder of the git processes this run has started and that are still going: who wrote it, and each git's
// id and start time. A SavePoint that is stopped from outside cannot end its gits on every system, so the next start reads the note and
// ends the ones that are still the very process it names. The folder belongs to one user, as does the single running copy of SavePoint.
internal static class RunningGitNote
{
    public const string FileName = "running-git.json";

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

    // A scanner can hold the file for a moment, so a few tries are made.
    public static void Clear(string path)
    {
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
