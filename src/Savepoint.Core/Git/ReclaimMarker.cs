using System.Text;
using System.Text.Json;

namespace Savepoint.Core.Git;

// A note in the git directory that a reclaim has started and not ended: which SavePoint is running it, and the git process it started
// last. A SavePoint that is killed cannot take git with it on every system, so the next start reads the note and waits for that git.
internal static class ReclaimMarker
{
    public const string FileName = "savepoint-reclaim.json";

    public static string PathIn(string gitDir) => Path.Combine(gitDir, FileName);

    // The reclaim is about to start git: this process owns it, and no git has been started yet.
    public static void Begin(string gitDir) => Write(gitDir, ProcessStamp.Current(), null);

    // The owner has started another git process of the reclaim.
    public static void Record(string gitDir, ProcessStamp git) => Write(gitDir, Read(gitDir)?.Owner ?? ProcessStamp.Current(), git);

    public static void Write(string gitDir, ProcessStamp owner, ProcessStamp? git)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            WriteStamp(writer, "owner", owner);
            if (git is { } started)
            {
                WriteStamp(writer, "git", started);
            }

            writer.WriteEndObject();
        }

        AtomicText.Write(PathIn(gitDir), Encoding.UTF8.GetString(buffer.ToArray()));
    }

    // Null when there is no note or it cannot be understood: a note that cannot be read must not hold anything up.
    public static (ProcessStamp Owner, ProcessStamp? Git)? Read(string gitDir)
    {
        if (AtomicText.ReadOrNull(PathIn(gitDir)) is not { } text)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(text);
            var root = document.RootElement;
            return ReadStamp(root, "owner") is { } owner ? (owner, ReadStamp(root, "git")) : null;
        }
        catch (Exception e) when (e is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
        {
            return null;
        }
    }

    // A scanner can hold the file for a moment, so a few tries are made.
    public static void Clear(string gitDir)
    {
        for (var attempt = 1; attempt <= 4; attempt++)
        {
            try
            {
                File.Delete(PathIn(gitDir));
                return;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(30 * attempt);
            }
        }
    }

    private static void WriteStamp(Utf8JsonWriter writer, string name, ProcessStamp stamp)
    {
        writer.WriteStartObject(name);
        writer.WriteNumber("id", stamp.Id);
        writer.WriteNumber("startedAtUtcTicks", stamp.StartedAtUtcTicks);
        writer.WriteEndObject();
    }

    private static ProcessStamp? ReadStamp(JsonElement root, string name) =>
        root.TryGetProperty(name, out var element) ? new ProcessStamp(element.GetProperty("id").GetInt32(), element.GetProperty("startedAtUtcTicks").GetInt64()) : null;
}
