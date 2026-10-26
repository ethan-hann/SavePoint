using System.Text;

namespace Savepoint.Core.CloudSaves;

/// <summary>Reads files a store may have open, without ever asking for more than read access.</summary>
internal static class SharedFile
{
    /// <summary>Opens a file for reading while the store that owns it keeps it open, and lets it be replaced or deleted meanwhile.</summary>
    public static FileStream OpenRead(string path) => new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);

    /// <summary>The file's text, or null when it does not exist or is longer than <paramref name="limit"/> bytes.</summary>
    public static string? ReadText(string path, long limit)
    {
        if (!File.Exists(path))
        {
            return null;
        }

        using var stream = OpenRead(path);
        if (stream.Length > limit)
        {
            return null;
        }

        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }
}
