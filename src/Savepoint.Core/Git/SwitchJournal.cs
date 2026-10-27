using System.Text;
using System.Text.Json;

namespace Savepoint.Core.Git;

// A note in the git directory that a checkout has started and not finished. The app shows it, and the engine acts on it: a
// capture refuses a folder that has one, and only the switch it names may finish a half-switched folder.
internal static class SwitchJournal
{
    public const string FileName = "savepoint-switch.json";

    // The waits between tries when another program holds the note, for Read and Clear alike (D31.6).
    internal static readonly TimeSpan[] Waits =
    [
        TimeSpan.FromMilliseconds(50),
        TimeSpan.FromMilliseconds(100),
        TimeSpan.FromMilliseconds(200),
        TimeSpan.FromMilliseconds(400),
        TimeSpan.FromMilliseconds(800),
        TimeSpan.FromMilliseconds(1000),
    ];

    private static readonly UTF8Encoding _utf8 = new(encoderShouldEmitUTF8Identifier: false);

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
    // with no target: the folder may hold files of two points and nothing can say which. The waits between tries happen in delay
    // (Task.Delay by default), so the caller's thread is free while another program holds the note; a hold that never ends costs the
    // whole of Waits and no thread.
    public static async Task<InterruptedSwitch?> ReadAsync(string gitDir, Func<TimeSpan, CancellationToken, Task>? delay = null, CancellationToken ct = default)
    {
        if (!File.Exists(PathIn(gitDir)))
        {
            return null;
        }

        var unknown = new InterruptedSwitch(null, null);
        string text;
        try
        {
            text = await ReadTextAsync(PathIn(gitDir), delay ?? Task.Delay, ct).ConfigureAwait(false);
        }
        catch (FileNotFoundException)
        {
            // Cleared while it was being read.
            return null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
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

    // The same read for code with no async context, where the waits block the thread: a test passes pause to act in them, and a caller
    // that cannot afford to wait passes one that returns at once.
    public static InterruptedSwitch? Read(string gitDir, Action<TimeSpan>? pause = null) =>
        ReadAsync(gitDir, Blocking(pause)).GetAwaiter().GetResult();

    private static async Task<string> ReadTextAsync(string path, Func<TimeSpan, CancellationToken, Task> delay, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return File.ReadAllText(path, _utf8);
            }
            catch (Exception e) when (attempt < Waits.Length && e is IOException and not FileNotFoundException and not DirectoryNotFoundException)
            {
                await delay(Waits[attempt], ct).ConfigureAwait(false);
            }
        }
    }

    // False when the note is still there after the last try: it makes captures refuse until a switch or a discard clears it.
    public static async Task<bool> ClearAsync(string gitDir, Func<TimeSpan, CancellationToken, Task>? delay = null)
    {
        delay ??= Task.Delay;
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                File.Delete(PathIn(gitDir));
                return true;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                if (attempt >= Waits.Length)
                {
                    return false;
                }

                await delay(Waits[attempt], CancellationToken.None).ConfigureAwait(false);
            }
        }
    }

    // The same clear for code with no async context; see Read.
    public static bool Clear(string gitDir, Action<TimeSpan>? pause = null) =>
        ClearAsync(gitDir, Blocking(pause)).GetAwaiter().GetResult();

    // Every wait happens inside the call, so the task it returns is already complete and the caller never has to be woken.
    private static Func<TimeSpan, CancellationToken, Task> Blocking(Action<TimeSpan>? pause)
    {
        pause ??= Thread.Sleep;
        return (wait, _) =>
        {
            pause(wait);
            return Task.CompletedTask;
        };
    }
}
