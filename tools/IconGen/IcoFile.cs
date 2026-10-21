namespace Savepoint.Tools.IconGen;

// An .ico container around PNG images, which Windows reads from Vista on. SkiaSharp has no .ico encoder.
internal static class IcoFile
{
    internal static byte[] Build(IReadOnlyList<(int Size, byte[] Png)> images)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        writer.Write((ushort)0);
        writer.Write((ushort)1);
        writer.Write((ushort)images.Count);

        var offset = 6 + (16 * images.Count);
        foreach (var (size, png) in images)
        {
            // A width or height of 256 is stored as 0.
            var stored = (byte)(size >= 256 ? 0 : size);
            writer.Write(stored);
            writer.Write(stored);
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((ushort)1);
            writer.Write((ushort)32);
            writer.Write((uint)png.Length);
            writer.Write((uint)offset);
            offset += png.Length;
        }

        foreach (var (_, png) in images)
        {
            writer.Write(png);
        }

        writer.Flush();
        return stream.ToArray();
    }
}
