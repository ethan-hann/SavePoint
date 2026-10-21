using System.Buffers.Binary;

namespace Savepoint.Core.Artwork;

/// <summary>The image formats SavePoint accepts for game artwork.</summary>
public enum ImageFormat
{
    /// <summary>PNG.</summary>
    Png,

    /// <summary>JPEG.</summary>
    Jpeg,

    /// <summary>WebP, lossy or lossless.</summary>
    WebP,
}

/// <summary>What a file's headers say about an image that passed the structure check.</summary>
/// <param name="Format">Which of the accepted formats the file is.</param>
/// <param name="Width">Width in pixels, at least 1.</param>
/// <param name="Height">Height in pixels, at least 1.</param>
public readonly record struct ImageInfo(ImageFormat Format, int Width, int Height);

/// <summary>
/// Checks that a file is a whole PNG, JPEG, or WebP image and reads its size, without decoding any pixels. SavePoint uses it to
/// decide whether a downloaded or chosen file is an image worth keeping (D30); the app still decodes the pixels when it draws.
/// </summary>
/// <remarks>
/// The check is structural: the signature, the size fields, and the end of the file must all be where the format puts them, so a
/// web page saved under an image name, a file of another type, and a download cut short are all refused. An image wider or taller
/// than 32,767 pixels, or with more than 100 million pixels, is refused too, since drawing it would cost more memory than a piece
/// of artwork is worth. It does not prove every byte of pixel data is sound.
/// </remarks>
public static class ImageProbe
{
    private const int _maxSide = 32_767;
    private const long _maxPixels = 100_000_000;
    private static readonly byte[] _pngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    /// <summary>Reads the image in a seekable stream from its first byte.</summary>
    /// <param name="stream">A readable, seekable stream; it is not closed.</param>
    /// <returns>The format and size, or null when the stream is not a complete PNG, JPEG, or WebP image.</returns>
    public static ImageInfo? Read(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead || !stream.CanSeek)
        {
            return null;
        }

        try
        {
            var length = stream.Length;
            Span<byte> head = stackalloc byte[12];
            if (!ReadAt(stream, 0, head))
            {
                return null;
            }

            var info = head[..8].SequenceEqual(_pngSignature) ? ReadPng(stream, length)
                : head[0] == 0xFF && head[1] == 0xD8 ? ReadJpeg(stream, length)
                : head[..4].SequenceEqual("RIFF"u8) && head[8..12].SequenceEqual("WEBP"u8) ? ReadWebP(stream, length)
                : null;
            return info is { } found && Acceptable(found.Width, found.Height) ? info : null;
        }
        catch (Exception e) when (e is IOException or NotSupportedException or ObjectDisposedException)
        {
            return null;
        }
    }

    /// <summary>Reads the image in a file.</summary>
    /// <param name="path">The file to read.</param>
    /// <returns>The format and size, or null when the file is missing, unreadable, or not a complete PNG, JPEG, or WebP image.</returns>
    public static ImageInfo? ReadFile(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            return Read(stream);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return null;
        }
    }

    private static bool Acceptable(int width, int height) =>
        width is >= 1 and <= _maxSide && height is >= 1 and <= _maxSide && (long)width * height <= _maxPixels;

    // Reads exactly buffer.Length bytes at a position; false when the stream ends first.
    private static bool ReadAt(Stream stream, long position, Span<byte> buffer)
    {
        if (position < 0 || position > stream.Length)
        {
            return false;
        }

        stream.Position = position;
        return stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false) == buffer.Length;
    }

    private static ImageInfo? ReadPng(Stream stream, long length)
    {
        // IHDR comes first, is 13 bytes long, and its checksum is checked; then chunks are walked by their lengths to IEND.
        Span<byte> header = stackalloc byte[8 + 13 + 4];
        if (!ReadAt(stream, 8, header) || BinaryPrimitives.ReadUInt32BigEndian(header) != 13 || !header[4..8].SequenceEqual("IHDR"u8))
        {
            return null;
        }

        if (Crc32(header.Slice(4, 4 + 13)) != BinaryPrimitives.ReadUInt32BigEndian(header[21..]))
        {
            return null;
        }

        var width = BinaryPrimitives.ReadInt32BigEndian(header[8..]);
        var height = BinaryPrimitives.ReadInt32BigEndian(header[12..]);
        var seenData = false;
        var position = 8L + 8 + 13 + 4;
        Span<byte> chunk = stackalloc byte[8];
        while (ReadAt(stream, position, chunk))
        {
            var size = BinaryPrimitives.ReadUInt32BigEndian(chunk);
            var next = position + 12 + size;
            if (next > length)
            {
                return null;
            }

            if (chunk[4..8].SequenceEqual("IEND"u8))
            {
                return size == 0 && next == length && seenData && Acceptable(width, height) ? new ImageInfo(ImageFormat.Png, width, height) : null;
            }

            seenData |= chunk[4..8].SequenceEqual("IDAT"u8);
            position = next;
        }

        return null;
    }

    private static ImageInfo? ReadJpeg(Stream stream, long length)
    {
        // Segments are walked by their lengths until the scan starts; the frame header holds the size. The file must end with the
        // end-of-image marker, which tells a whole file from one cut short.
        if (!EndsWithEndOfImage(stream, length))
        {
            return null;
        }

        Span<byte> marker = stackalloc byte[4];
        Span<byte> frame = stackalloc byte[5];
        var position = 2L;
        while (true)
        {
            if (!ReadAt(stream, position, marker[..2]) || marker[0] != 0xFF)
            {
                return null;
            }

            var code = marker[1];
            if (code == 0xFF)
            {
                position++;
                continue;
            }

            if (code is 0x01 or (>= 0xD0 and <= 0xD7))
            {
                position += 2;
                continue;
            }

            if (code is 0xD8 or 0xD9 or 0x00)
            {
                return null;
            }

            if (!ReadAt(stream, position + 2, marker[..2]))
            {
                return null;
            }

            var size = BinaryPrimitives.ReadUInt16BigEndian(marker);
            if (size < 2 || position + 2 + size > length)
            {
                return null;
            }

            if (code is >= 0xC0 and <= 0xCF and not (0xC4 or 0xC8 or 0xCC))
            {
                if (size < 8 || !ReadAt(stream, position + 4, frame))
                {
                    return null;
                }

                var height = BinaryPrimitives.ReadUInt16BigEndian(frame[1..]);
                var width = BinaryPrimitives.ReadUInt16BigEndian(frame[3..]);
                return new ImageInfo(ImageFormat.Jpeg, width, height);
            }

            if (code == 0xDA)
            {
                // The scan came before any frame header.
                return null;
            }

            position += 2 + size;
        }
    }

    // Some encoders pad a file with zero bytes after the end-of-image marker; nothing else may follow it.
    private static bool EndsWithEndOfImage(Stream stream, long length)
    {
        Span<byte> tail = stackalloc byte[1];
        Span<byte> pair = stackalloc byte[2];
        for (var position = length - 1; position >= 1 && length - position <= 16; position--)
        {
            if (!ReadAt(stream, position, tail))
            {
                return false;
            }

            if (tail[0] == 0)
            {
                continue;
            }

            return tail[0] == 0xD9 && ReadAt(stream, position - 1, pair) && pair[0] == 0xFF;
        }

        return false;
    }

    private static ImageInfo? ReadWebP(Stream stream, long length)
    {
        Span<byte> riff = stackalloc byte[4];
        if (!ReadAt(stream, 4, riff))
        {
            return null;
        }

        // The size in the header counts everything after it, so a file shorter than that was cut short.
        var end = 8L + BinaryPrimitives.ReadUInt32LittleEndian(riff);
        if (end > length || end < 12 + 8)
        {
            return null;
        }

        Span<byte> chunk = stackalloc byte[8 + 16];
        if (!ReadAt(stream, 12, chunk[..8]))
        {
            return null;
        }

        var size = BinaryPrimitives.ReadUInt32LittleEndian(chunk[4..8]);
        if (20L + size > end)
        {
            return null;
        }

        var kind = chunk[..4];
        var data = chunk[8..];
        if (kind.SequenceEqual("VP8 "u8) && size >= 10 && ReadAt(stream, 20, data[..10]))
        {
            // A key frame starts with a 3-byte tag and the code 9D 01 2A, then two 14-bit sizes.
            return (data[0] & 1) == 0 && data[3] == 0x9D && data[4] == 0x01 && data[5] == 0x2A
                ? new ImageInfo(ImageFormat.WebP, BinaryPrimitives.ReadUInt16LittleEndian(data[6..]) & 0x3FFF, BinaryPrimitives.ReadUInt16LittleEndian(data[8..]) & 0x3FFF)
                : null;
        }

        if (kind.SequenceEqual("VP8L"u8) && size >= 5 && ReadAt(stream, 20, data[..5]))
        {
            var bits = BinaryPrimitives.ReadUInt32LittleEndian(data[1..]);
            return data[0] == 0x2F ? new ImageInfo(ImageFormat.WebP, (int)(bits & 0x3FFF) + 1, (int)((bits >> 14) & 0x3FFF) + 1) : null;
        }

        if (kind.SequenceEqual("VP8X"u8) && size >= 10 && ReadAt(stream, 20, data[..10]))
        {
            return new ImageInfo(ImageFormat.WebP, 1 + data[4] + (data[5] << 8) + (data[6] << 16), 1 + data[7] + (data[8] << 8) + (data[9] << 16));
        }

        return null;
    }

    private static uint Crc32(ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var value in data)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
        }

        return ~crc;
    }
}
