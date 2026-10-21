using System.Text;
using System.Text.Json;

namespace Savepoint.Core.Git;

// A note in the git directory that a checkout has started and not finished. It changes no behavior; it lets the app say so.
internal static class SwitchJournal
{
    public const string FileName = "savepoint-switch.json";

    public static string PathIn(string gitDir) => Path.Combine(gitDir, FileName);

    public static void Write(string gitDir, string? from, string to)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("from", from);
            writer.WriteString("to", to);
            writer.WriteEndObject();
        }

        AtomicText.Write(PathIn(gitDir), Encoding.UTF8.GetString(buffer.ToArray()));
    }

    // Null when no switch is unfinished, or the note cannot be understood (a note that cannot be read must not block anything).
    public static InterruptedSwitch? Read(string gitDir)
    {
        if (AtomicText.ReadOrNull(PathIn(gitDir)) is not { } text)
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(text);
            var root = document.RootElement;
            var to = root.GetProperty("to").GetString();
            var from = root.TryGetProperty("from", out var fromElement) ? fromElement.GetString() : null;
            return to is null ? null : new InterruptedSwitch(from, to);
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return null;
        }
    }

    public static void Clear(string gitDir)
    {
        try
        {
            File.Delete(PathIn(gitDir));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A note left behind only makes the status report an unfinished switch that the next checkout clears.
        }
    }
}
