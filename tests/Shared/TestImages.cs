using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace Savepoint.Testing;

// Linked into the test assemblies: small image files built by hand, so no test commits a binary. The PNG is a real image that any
// decoder reads. The JPEG and WebP have the structure of a real file (segments, sizes, end markers) but not pixel data a decoder
// could use, which is all the structure check needs and what a decoder must refuse.
internal static class TestImages
{
    private static readonly uint[] _crcTable = BuildCrcTable();

    internal static byte[] Png(int width, int height, uint argb = 0xFF336699)
    {
        var row = new byte[1 + (width * 4)];
        for (var x = 0; x < width; x++)
        {
            row[1 + (x * 4)] = (byte)(argb >> 16);
            row[2 + (x * 4)] = (byte)(argb >> 8);
            row[3 + (x * 4)] = (byte)argb;
            row[4 + (x * 4)] = (byte)(argb >> 24);
        }

        using var raw = new MemoryStream();
        using (var zlib = new ZLibStream(raw, CompressionLevel.Fastest, leaveOpen: true))
        {
            for (var y = 0; y < height; y++)
            {
                zlib.Write(row);
            }
        }

        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0), width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8;
        header[9] = 6;

        using var file = new MemoryStream();
        file.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        PngChunk(file, "IHDR", header);
        PngChunk(file, "IDAT", raw.ToArray());
        PngChunk(file, "IEND", []);
        return file.ToArray();
    }

    // A PNG whose IHDR claims dimensions the pixel data cannot back up: the structure is sound, a decoder refuses it.
    internal static byte[] PngWithHeaderOnly(int width, int height)
    {
        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0), width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8;
        header[9] = 6;
        using var file = new MemoryStream();
        file.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        PngChunk(file, "IHDR", header);
        PngChunk(file, "IEND", []);
        return file.ToArray();
    }

    // Baseline by default; progressive when the frame marker is 0xC2. Comment segments holding the given number of bytes sit before
    // the frame, as an Exif block does in a camera file; they also make a file as large as a test needs.
    internal static byte[] Jpeg(int width, int height, byte frameMarker = 0xC0, int padding = 0, int scanFill = 0)
    {
        using var file = new MemoryStream();
        file.Write([0xFF, 0xD8]);
        file.Write([0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01, 0x01, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00]);
        // Each comment segment holds at most 65,533 bytes; every one costs 4 bytes of marker and length on top of its content.
        for (var left = padding; left > 0;)
        {
            var size = Math.Min(left, 60_000);
            file.Write([0xFF, 0xFE]);
            var length = new byte[2];
            BinaryPrimitives.WriteUInt16BigEndian(length, (ushort)(size + 2));
            file.Write(length);
            file.Write(new byte[size]);
            left -= size;
        }

        var frame = new byte[19];
        frame[0] = 0xFF;
        frame[1] = frameMarker;
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(2), 17);
        frame[4] = 8;
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(5), (ushort)height);
        BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(7), (ushort)width);
        frame[9] = 3;
        frame[10] = 1;
        frame[11] = 0x22;
        frame[13] = 2;
        frame[14] = 0x11;
        frame[15] = 1;
        frame[16] = 3;
        frame[17] = 0x11;
        frame[18] = 1;
        file.Write(frame);
        file.Write([0xFF, 0xDA, 0x00, 0x0C, 0x03, 0x01, 0x00, 0x02, 0x11, 0x03, 0x11, 0x00, 0x3F, 0x00]);
        file.Write([0x12, 0x34, 0x56, 0x78, 0x9A, 0xBC]);
        file.Write(new byte[scanFill]);
        file.Write([0xFF, 0xD9]);
        return file.ToArray();
    }

    // Lossless, lossy and extended WebP files, each with the sizes a real one has.
    internal static byte[] WebPLossless(int width, int height)
    {
        var chunk = new byte[10];
        chunk[0] = 0x2F;
        var bits = (uint)(width - 1) | ((uint)(height - 1) << 14);
        BinaryPrimitives.WriteUInt32LittleEndian(chunk.AsSpan(1), bits);
        return WebP(("VP8L", chunk));
    }

    internal static byte[] WebPLossy(int width, int height)
    {
        var chunk = new byte[16];
        chunk[0] = 0x10;
        chunk[3] = 0x9D;
        chunk[4] = 0x01;
        chunk[5] = 0x2A;
        BinaryPrimitives.WriteUInt16LittleEndian(chunk.AsSpan(6), (ushort)width);
        BinaryPrimitives.WriteUInt16LittleEndian(chunk.AsSpan(8), (ushort)height);
        return WebP(("VP8 ", chunk));
    }

    internal static byte[] WebPExtended(int width, int height)
    {
        var extended = new byte[10];
        extended[0] = 0x10;
        WriteUInt24(extended.AsSpan(4), width - 1);
        WriteUInt24(extended.AsSpan(7), height - 1);
        var inner = new byte[10];
        inner[0] = 0x2F;
        return WebP(("VP8X", extended), ("VP8L", inner));
    }

    internal static byte[] Gif() => Encoding.ASCII.GetBytes("GIF89a\x01\x00\x01\x00\x80\x00\x00\x00\x00\x00\xFF\xFF\xFF\x3B");

    internal static byte[] Html() => Encoding.ASCII.GetBytes("<!DOCTYPE html><html><body>Not Found</body></html>");

    // A JPEG of exactly this many bytes, made by lengthening the scan data, which can be any run of bytes but 0xFF.
    internal static byte[] JpegOfSize(int width, int height, long totalBytes)
    {
        var bare = Jpeg(width, height);
        return Jpeg(width, height, scanFill: (int)(totalBytes - bare.Length));
    }

    // Writes the file a test points a game's artwork at, and returns its path.
    internal static string Write(string folder, string name, byte[] content)
    {
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, name);
        File.WriteAllBytes(path, content);
        return path;
    }

    private static byte[] WebP(params (string Name, byte[] Data)[] chunks)
    {
        using var body = new MemoryStream();
        body.Write(Encoding.ASCII.GetBytes("WEBP"));
        foreach (var (name, data) in chunks)
        {
            body.Write(Encoding.ASCII.GetBytes(name));
            var size = new byte[4];
            BinaryPrimitives.WriteUInt32LittleEndian(size, (uint)data.Length);
            body.Write(size);
            body.Write(data);
            if (data.Length % 2 == 1)
            {
                body.WriteByte(0);
            }
        }

        using var file = new MemoryStream();
        file.Write(Encoding.ASCII.GetBytes("RIFF"));
        var total = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(total, (uint)body.Length);
        file.Write(total);
        file.Write(body.ToArray());
        return file.ToArray();
    }

    private static void WriteUInt24(Span<byte> target, int value)
    {
        target[0] = (byte)value;
        target[1] = (byte)(value >> 8);
        target[2] = (byte)(value >> 16);
    }

    private static void PngChunk(Stream file, string type, byte[] data)
    {
        var length = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        file.Write(length);
        var body = new byte[4 + data.Length];
        Encoding.ASCII.GetBytes(type, body);
        data.CopyTo(body, 4);
        file.Write(body);
        var crc = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(crc, Crc32(body));
        file.Write(crc);
    }

    private static uint Crc32(ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var value in data)
        {
            crc = _crcTable[(crc ^ value) & 0xFF] ^ (crc >> 8);
        }

        return crc ^ 0xFFFFFFFFu;
    }

    private static uint[] BuildCrcTable()
    {
        var table = new uint[256];
        for (var n = 0u; n < 256; n++)
        {
            var c = n;
            for (var k = 0; k < 8; k++)
            {
                c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
            }

            table[n] = c;
        }

        return table;
    }
}
