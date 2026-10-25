using System.Buffers.Binary;
using System.Security.Cryptography;

namespace Savepoint.Core.Git;

/// <summary>
/// Reads a git index file just far enough to tell whether any entry carries the skip-worktree flag, without starting git. The answer is
/// one-sided: <see langword="false"/> is only returned when every entry was read, the file's own checksum matched, and no entry has the
/// flag; anything else (a version or extension this reader does not know, a short or damaged file, an unreadable file) answers
/// <see langword="true"/> so git is asked.
/// </summary>
internal static class IndexMarkScan
{
    private const int _fixedEntryBytes = 62;
    private const ushort _extendedFlag = 0x4000;
    private const ushort _skipWorktree = 0x4000;
    private const ushort _intentToAdd = 0x2000;
    private const int _trailerBytes = 20;

    /// <summary>Whether the index file at <paramref name="indexPath"/> may hold an entry marked skip-worktree.</summary>
    /// <param name="indexPath">Path of the index file.</param>
    /// <returns><see langword="false"/> only when the whole file was read and no entry carries the flag; otherwise <see langword="true"/>.</returns>
    public static bool MayHoldMarks(string indexPath)
    {
        try
        {
            using var file = new FileStream(indexPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, bufferSize: 1);
            if (file.Length < 12 + _trailerBytes)
            {
                return true;
            }

            using var reader = new Reader(file);
            return !TryProveNone(reader);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    // True only when the file was read to its end and nothing in it is marked.
    private static bool TryProveNone(Reader reader)
    {
        Span<byte> header = stackalloc byte[12];
        reader.ReadExactly(header);
        if (header[0] != (byte)'D' || header[1] != (byte)'I' || header[2] != (byte)'R' || header[3] != (byte)'C')
        {
            return false;
        }

        var version = BinaryPrimitives.ReadUInt32BigEndian(header[4..]);
        if (version is < 2 or > 4)
        {
            return false;
        }

        var count = BinaryPrimitives.ReadUInt32BigEndian(header[8..]);
        Span<byte> fixedPart = stackalloc byte[_fixedEntryBytes];
        Span<byte> extended = stackalloc byte[2];
        for (uint entry = 0; entry < count; entry++)
        {
            reader.ReadExactly(fixedPart);
            long entryBytes = _fixedEntryBytes;
            var flags = BinaryPrimitives.ReadUInt16BigEndian(fixedPart[60..]);
            if ((flags & _extendedFlag) != 0)
            {
                // Version 2 has no room for these, so a file that claims them is not one this reader understands.
                if (version == 2)
                {
                    return false;
                }

                reader.ReadExactly(extended);
                entryBytes += 2;
                var more = BinaryPrimitives.ReadUInt16BigEndian(extended);
                if ((more & _skipWorktree) != 0 || (more & ~(_skipWorktree | _intentToAdd)) != 0)
                {
                    return false;
                }
            }

            if (version == 4)
            {
                // The name is a count of bytes to cut from the last name (one to ten bytes), then the rest of it, ended by a NUL.
                var cut = 0;
                int b;
                do
                {
                    b = reader.ReadByte();
                    if (b < 0 || ++cut > 10)
                    {
                        return false;
                    }
                }
                while ((b & 0x80) != 0);

                if (!reader.SkipToNul(out _))
                {
                    return false;
                }

                continue;
            }

            if (!reader.SkipToNul(out var nameBytes))
            {
                return false;
            }

            // Entries are padded with NULs to a multiple of eight bytes, and at least one NUL ends the name.
            var total = entryBytes + nameBytes;
            var padding = ((total + 8) & ~7L) - total - 1;
            for (long i = 0; i < padding; i++)
            {
                if (reader.ReadByte() != 0)
                {
                    return false;
                }
            }
        }

        return EndsAfterKnownExtensions(reader);
    }

    // After the entries come extensions (four-byte name, four-byte size, body) and the file's checksum. A split index keeps entries in a
    // second file, and a sparse one stands in folders for entries, so neither can be answered from this file alone.
    private static bool EndsAfterKnownExtensions(Reader reader)
    {
        Span<byte> head = stackalloc byte[8];
        while (reader.Remaining > _trailerBytes)
        {
            if (reader.Remaining - _trailerBytes < 8)
            {
                return false;
            }

            reader.ReadExactly(head);
            var size = BinaryPrimitives.ReadUInt32BigEndian(head[4..]);
            if (size > reader.Remaining - _trailerBytes || head[..4].SequenceEqual("link"u8) || head[..4].SequenceEqual("sdir"u8))
            {
                return false;
            }

            reader.Skip(size);
        }

        return reader.Remaining == _trailerBytes && reader.ChecksumMatches();
    }

    // Reads a file in large blocks and hashes everything before the final 20 bytes, which is where git keeps the file's own checksum.
    private sealed class Reader(FileStream file) : IDisposable
    {
        private readonly byte[] _block = new byte[64 * 1024];
        private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA1);
        private readonly long _length = file.Length;
        private long _loaded;
        private int _at;
        private int _have;

        public long Remaining => _length - _loaded + (_have - _at);

        public void Dispose() => _hash.Dispose();

        public int ReadByte()
        {
            if (_at == _have && !Fill())
            {
                return -1;
            }

            return _block[_at++];
        }

        public void ReadExactly(Span<byte> into)
        {
            for (var i = 0; i < into.Length; i++)
            {
                var b = ReadByte();
                if (b < 0)
                {
                    throw new EndOfStreamException();
                }

                into[i] = (byte)b;
            }
        }

        public void Skip(long bytes)
        {
            for (long i = 0; i < bytes; i++)
            {
                if (ReadByte() < 0)
                {
                    throw new EndOfStreamException();
                }
            }
        }

        // Counts the bytes before the next NUL; the NUL is consumed.
        public bool SkipToNul(out long before)
        {
            before = 0;
            int b;
            while ((b = ReadByte()) > 0)
            {
                before++;
            }

            return b == 0;
        }

        public bool ChecksumMatches()
        {
            Span<byte> stored = stackalloc byte[_trailerBytes];
            ReadExactly(stored);
            Span<byte> computed = stackalloc byte[_trailerBytes];
            _hash.GetCurrentHash(computed);

            // index.skipHash writes zeros instead of a checksum.
            return stored.SequenceEqual(computed) || !stored.ContainsAnyExcept((byte)0);
        }

        private bool Fill()
        {
            var read = file.Read(_block, 0, _block.Length);
            if (read <= 0)
            {
                return false;
            }

            var hashed = (int)Math.Min(read, Math.Max(0, _length - _trailerBytes - _loaded));
            if (hashed > 0)
            {
                _hash.AppendData(_block, 0, hashed);
            }

            _loaded += read;
            _at = 0;
            _have = read;
            return true;
        }
    }
}
