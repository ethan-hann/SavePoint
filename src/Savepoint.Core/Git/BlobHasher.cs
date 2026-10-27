using System.Security.Cryptography;
using System.Text;

namespace Savepoint.Core.Git;

internal enum HashOutcome
{
    Hashed,
    Vanished,
    Unreadable,
}

// The id git gives a file's content: SHA-1 of "blob <length>" and a NUL, then the bytes. The repository's rules keep files unconverted, so the id depends on the bytes alone.
internal static class BlobHasher
{
    private const int _bufferSize = 128 * 1024;

    // The id of bytes already in memory, such as the text of a link.
    public static string IdOf(ReadOnlySpan<byte> content)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
        hash.AppendData(Encoding.ASCII.GetBytes($"blob {content.Length}\0"));
        hash.AppendData(content);
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    // The token is looked at between reads, so a big file does not keep a cancelled call waiting for the rest of it.
    public static HashOutcome TryHash(string path, out string id, CancellationToken ct = default)
    {
        id = string.Empty;
        try
        {
            // Every kind of sharing is allowed: a game that has the file open must not stop the preview.
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, _bufferSize, FileOptions.SequentialScan);
            var length = stream.Length;
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
            hash.AppendData(Encoding.ASCII.GetBytes($"blob {length}\0"));

            var buffer = new byte[_bufferSize];
            var remaining = length;
            while (remaining > 0)
            {
                ct.ThrowIfCancellationRequested();
                var read = stream.Read(buffer, 0, (int)Math.Min(buffer.Length, remaining));
                if (read == 0)
                {
                    // Shorter than it was a moment ago: it is being rewritten, so it cannot be compared.
                    return HashOutcome.Unreadable;
                }

                hash.AppendData(buffer, 0, read);
                remaining -= read;
            }

            id = Convert.ToHexStringLower(hash.GetHashAndReset());
            return HashOutcome.Hashed;
        }
        catch (Exception e) when (e is FileNotFoundException or DirectoryNotFoundException)
        {
            return HashOutcome.Vanished;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return HashOutcome.Unreadable;
        }
    }
}
