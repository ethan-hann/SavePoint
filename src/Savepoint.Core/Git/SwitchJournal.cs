using System.Text;
using System.Text.Json;

namespace Savepoint.Core.Git;

// A note in the git directory that a checkout has started and not finished. The app shows it, and the engine acts on it: a
// capture refuses a folder that has one, and only the switch it names may finish a half-switched folder.
internal static class SwitchJournal
{
    public const string FileName = "savepoint-switch.json";

    public static string PathIn(string gitDir) => Path.Combine(gitDir, FileName);

    public static void Write(string gitDir, string? from, string to, string? branch = null)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("from", from);
            writer.WriteString("to", to);
            if (branch is not null)
            {
                writer.WriteString("branch", branch);
            }

            writer.WriteEndObject();
        }

        AtomicText.Write(PathIn(gitDir), Encoding.UTF8.GetString(buffer.ToArray()));
    }

    // Null when no note exists. A note that exists but cannot be read or understood still says a switch started, so it is returned
    // with no target: the folder may hold files of two points and nothing can say which.
    public static InterruptedSwitch? Read(string gitDir)
    {
        if (!File.Exists(PathIn(gitDir)))
        {
            return null;
        }

        var unknown = new InterruptedSwitch(null, null);
        if (AtomicText.ReadOrNull(PathIn(gitDir)) is not { } text)
        {
            return unknown;
        }

        try
        {
            using var document = JsonDocument.Parse(text);
            var root = document.RootElement;
            var to = root.GetProperty("to").GetString();
            var from = root.TryGetProperty("from", out var fromElement) ? fromElement.GetString() : null;
            var branch = root.TryGetProperty("branch", out var branchElement) ? branchElement.GetString() : null;
            return to is null ? unknown : new InterruptedSwitch(from, to, branch);
        }
        catch (Exception e) when (e is JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return unknown;
        }
    }

    // A scanner can hold the file for a moment, so a few tries are made; a note that stays makes captures refuse until a switch or a discard clears it.
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
}
