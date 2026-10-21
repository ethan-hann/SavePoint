using System.Buffers.Binary;
using System.Text;

namespace Savepoint.Core.Tests.Services.Exe;

// One language's string table inside a version resource.
internal sealed record PeStringTable(ushort Lcid, ushort CodePage, params (string Key, string Value)[] Strings)
{
    public static PeStringTable English(params (string Key, string Value)[] strings) => new(0x0409, 0x04B0, strings);
}

// Builds tiny but well-formed PE32+ images by hand, so each version-resource shape the reader must cope with is exact.
internal static class PeFixture
{
    private const int _rtVersion = 16;
    private const int _rtManifest = 24;
    private const int _fileAlignment = 0x200;
    private const int _sectionAlignment = 0x1000;

    public static byte[] WithVersion(Version? fileVersion, Version? productVersion, params PeStringTable[] tables) =>
        Build(rsrc: ResourceSection(VersionBlob(fileVersion, productVersion, tables), _rtVersion, tables.Length > 0 ? tables[0].Lcid : (ushort)0x0409));

    // A real resource directory that holds something other than a version.
    public static byte[] WithOnlyAManifest() => Build(rsrc: ResourceSection("<assembly/>"u8.ToArray(), _rtManifest, 0x0409));

    public static byte[] WithoutResources() => Build(rsrc: null);

    private static byte[] VersionBlob(Version? fileVersion, Version? productVersion, PeStringTable[] tables)
    {
        var fixedInfo = new byte[52];
        BinaryPrimitives.WriteUInt32LittleEndian(fixedInfo.AsSpan(0), 0xFEEF04BD);
        BinaryPrimitives.WriteUInt32LittleEndian(fixedInfo.AsSpan(4), 0x00010000);
        WriteVersion(fixedInfo.AsSpan(8), fileVersion);
        WriteVersion(fixedInfo.AsSpan(16), productVersion);

        var children = new MemoryStream();
        if (tables.Length > 0)
        {
            var tableBytes = new MemoryStream();
            foreach (var table in tables)
            {
                var strings = new MemoryStream();
                foreach (var (key, value) in table.Strings)
                {
                    var text = Encoding.Unicode.GetBytes(value + "\0");
                    Append(strings, Entry(key, (ushort)(text.Length / 2), 1, text, []));
                }

                Append(tableBytes, Entry($"{table.Lcid:X4}{table.CodePage:X4}", 0, 1, [], strings.ToArray()));
            }

            Append(children, Entry("StringFileInfo", 0, 1, [], tableBytes.ToArray()));
        }

        return Entry("VS_VERSION_INFO", 52, 0, fixedInfo, children.ToArray());
    }

    private static void WriteVersion(Span<byte> destination, Version? version)
    {
        if (version is null)
        {
            return;
        }

        BinaryPrimitives.WriteUInt32LittleEndian(destination, ((uint)version.Major << 16) | (uint)version.Minor);
        BinaryPrimitives.WriteUInt32LittleEndian(destination[4..], ((uint)Math.Max(0, version.Build) << 16) | (uint)Math.Max(0, version.Revision));
    }

    // wLength, wValueLength, wType, key, padding, value, padding, children; every part starts on a four-byte boundary.
    private static byte[] Entry(string key, ushort valueLength, ushort type, byte[] value, byte[] children)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.Unicode);
        writer.Write((ushort)0);
        writer.Write(valueLength);
        writer.Write(type);
        writer.Write(Encoding.Unicode.GetBytes(key + "\0"));
        Align(stream);
        writer.Write(value);
        Align(stream);
        writer.Write(children);
        writer.Flush();
        var bytes = stream.ToArray();
        BinaryPrimitives.WriteUInt16LittleEndian(bytes, (ushort)bytes.Length);
        return bytes;
    }

    // Siblings sit on four-byte boundaries; the length of an entry does not count the padding after it.
    private static void Append(MemoryStream stream, byte[] entry)
    {
        Align(stream);
        stream.Write(entry);
    }

    private static void Align(Stream stream)
    {
        while (stream.Position % 4 != 0)
        {
            stream.WriteByte(0);
        }
    }

    // Root -> type -> id -> language directories, then the data entry and the blob; offsets count from the section start.
    private static byte[] ResourceSection(byte[] blob, int typeId, ushort language)
    {
        const int dataEntryOffset = 0x48;
        const int blobOffset = 0x58;
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        WriteDirectory(writer, id: typeId, target: 0x80000000u | 0x18);
        WriteDirectory(writer, id: 1, target: 0x80000000u | 0x30);
        WriteDirectory(writer, id: language, target: dataEntryOffset);
        writer.Write((uint)(_sectionAlignment + blobOffset));
        writer.Write((uint)blob.Length);
        writer.Write(0u);
        writer.Write(0u);
        writer.Write(blob);
        return stream.ToArray();
    }

    private static void WriteDirectory(BinaryWriter writer, int id, uint target)
    {
        writer.Write(0u);
        writer.Write(0u);
        writer.Write((ushort)4);
        writer.Write((ushort)0);
        writer.Write((ushort)0);
        writer.Write((ushort)1);
        writer.Write((uint)id);
        writer.Write(target);
    }

    private static byte[] Build(byte[]? rsrc)
    {
        var sectionData = rsrc ?? new byte[16];
        var rawSize = Round(sectionData.Length, _fileAlignment);
        var virtualSize = sectionData.Length;
        var imageSize = _sectionAlignment + Round(virtualSize, _sectionAlignment);

        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);

        // DOS header: only the magic and the offset of the PE header matter.
        writer.Write((ushort)0x5A4D);
        writer.Write(new byte[0x3A]);
        writer.Write(0x40u);

        writer.Write(0x00004550u);
        writer.Write((ushort)0x8664);
        writer.Write((ushort)1);
        writer.Write(0u);
        writer.Write(0u);
        writer.Write(0u);
        writer.Write((ushort)0xF0);
        writer.Write((ushort)0x0022);

        writer.Write((ushort)0x20B);
        writer.Write((byte)14);
        writer.Write((byte)0);
        writer.Write(0u);
        writer.Write((uint)(rsrc is null ? 0 : rawSize));
        writer.Write(0u);
        writer.Write(0u);
        writer.Write(0u);
        writer.Write(0x140000000UL);
        writer.Write((uint)_sectionAlignment);
        writer.Write((uint)_fileAlignment);
        writer.Write(new byte[12]);
        writer.Write(0u);
        writer.Write((uint)imageSize);
        writer.Write((uint)_fileAlignment);
        writer.Write(0u);
        writer.Write((ushort)3);
        writer.Write((ushort)0x8160);
        writer.Write(0x100000UL);
        writer.Write(0x1000UL);
        writer.Write(0x100000UL);
        writer.Write(0x1000UL);
        writer.Write(0u);
        writer.Write(16u);
        for (var i = 0; i < 16; i++)
        {
            var isResources = i == 2 && rsrc is not null;
            writer.Write(isResources ? (uint)_sectionAlignment : 0u);
            writer.Write(isResources ? (uint)virtualSize : 0u);
        }

        writer.Write(Encoding.ASCII.GetBytes(rsrc is null ? ".text\0\0\0" : ".rsrc\0\0\0"));
        writer.Write((uint)virtualSize);
        writer.Write((uint)_sectionAlignment);
        writer.Write((uint)rawSize);
        writer.Write((uint)_fileAlignment);
        writer.Write(0u);
        writer.Write(0u);
        writer.Write((ushort)0);
        writer.Write((ushort)0);
        writer.Write(rsrc is null ? 0x60000020u : 0x40000040u);

        writer.Write(new byte[_fileAlignment - (int)stream.Position]);
        writer.Write(sectionData);
        writer.Write(new byte[rawSize - sectionData.Length]);
        return stream.ToArray();
    }

    private static int Round(int value, int alignment) => (value + alignment - 1) / alignment * alignment;
}
