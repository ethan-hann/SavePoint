using System.Text;

namespace Savepoint.Core.Git;

// Replaces a small text file in one step, so a reader (or a crash) never sees half of it.
internal static class AtomicText
{
    private const int _attempts = 5;
    private static readonly UTF8Encoding _utf8 = new(encoderShouldEmitUTF8Identifier: false);

    public static string? ReadOrNull(string path)
    {
        try
        {
            return File.Exists(path) ? File.ReadAllText(path, _utf8) : null;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    // Writes only when the content differs, so a steady state never touches the disk.
    public static bool WriteIfChanged(string path, string content)
    {
        if (ReadOrNull(path) == content)
        {
            return false;
        }

        Write(path, content);
        return true;
    }

    public static void Write(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temp = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            File.WriteAllText(temp, content, _utf8);
            Move(temp, path);
        }
        catch
        {
            TryDelete(temp);
            throw;
        }
    }

    // A git process that is reading the target for a moment makes the replace fail on Windows; it succeeds a moment later.
    private static void Move(string from, string to)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                File.Move(from, to, overwrite: true);
                return;
            }
            catch (Exception e) when (attempt < _attempts && e is IOException or UnauthorizedAccessException)
            {
                Thread.Sleep(40 * attempt);
            }
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // The original failure is the one worth reporting.
        }
    }
}
