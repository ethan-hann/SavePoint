using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Savepoint.Core.Paths;

// The Linux and macOS calls that say which file or folder a path reaches: the device and inode of what stat finds with links followed.
// The struct is read from a byte buffer at fixed offsets, because a C struct's layout is the one thing a P/Invoke cannot check.
// Any failure (a missing path, a libc without the call) is a null identity, and the caller falls back to the path and the consent marker.
internal static class UnixFiles
{
    // Linux statx: one layout on every architecture, 256 bytes. Offsets of stx_ino and the four device numbers.
    private const int _statxSize = 256;
    private const int _statxIno = 32;
    private const int _statxDevMajor = 136;
    private const int _statxDevMinor = 140;
    private const int _statxMaskOffset = 0;
    private const uint _statxIno64 = 0x100;
    private const int _atFdCwd = -100;

    // statfs: only f_type, the first field, is read; the buffer is larger than any architecture's struct.
    private const int _statfsSize = 256;
    private const long _msdosSuperMagic = 0x4d44;
    private const long _exfatSuperMagic = 0x2011BAB0;

    // macOS stat with 64-bit inodes: st_dev is a 32-bit number at 0 and st_ino a 64-bit number at 8, on x64 and arm64 alike.
    private const int _macStatSize = 256;
    private const int _macStatDev = 0;
    private const int _macStatIno = 8;

    // True for a file system type whose inodes stay put between reads. FAT and exFAT make inode numbers up as directory entries are
    // cached, so the same folder can report another one after the cache drops it.
    public static bool KeepsInodes(long fileSystemType) => (fileSystemType & 0xFFFFFFFFL) is not (_msdosSuperMagic or _exfatSuperMagic);

    // The file system type of the one holding the path, or null when it cannot be read.
    [SupportedOSPlatform("linux")]
    public static long? ReadLinuxFileSystemType(string path)
    {
        try
        {
            var buffer = new byte[_statfsSize];
            if (LinuxStatfs(path, buffer) != 0)
            {
                return null;
            }

            // f_type is the first field and as wide as a C long.
            return IntPtr.Size == 8 ? BitConverter.ToInt64(buffer, 0) : BitConverter.ToInt32(buffer, 0);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }

    [SupportedOSPlatform("linux")]
    public static FileIdentity? ReadLinuxIdentity(string path)
    {
        try
        {
            var buffer = new byte[_statxSize];
            if (LinuxStatx(_atFdCwd, path, 0, _statxIno64, buffer) != 0)
            {
                return null;
            }

            // A file system that cannot name an inode leaves the bit unset; an identity of zero would match every such folder.
            if ((BitConverter.ToUInt32(buffer, _statxMaskOffset) & _statxIno64) == 0)
            {
                return null;
            }

            var major = BitConverter.ToUInt32(buffer, _statxDevMajor);
            var minor = BitConverter.ToUInt32(buffer, _statxDevMinor);
            return new FileIdentity(((ulong)major << 32) | minor, BitConverter.ToUInt64(buffer, _statxIno));
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }

    [SupportedOSPlatform("macos")]
    public static FileIdentity? ReadMacIdentity(string path)
    {
        var buffer = new byte[_macStatSize];
        int result;
        try
        {
            // Intel Macs keep a 32-bit-inode stat under the plain name; Apple Silicon has only the plain name, and it is the 64-bit one.
            // Each read tries the suffixed name first and the plain one once, so no state outlives the call and nothing recurses.
            result = StatMac(path, buffer);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }

        return result != 0
            ? null
            : new FileIdentity(BitConverter.ToUInt32(buffer, _macStatDev), BitConverter.ToUInt64(buffer, _macStatIno));
    }

    [SupportedOSPlatform("macos")]
    private static int StatMac(string path, byte[] buffer)
    {
        try
        {
            return MacStat(path, buffer);
        }
        catch (EntryPointNotFoundException)
        {
            return MacStatPlain(path, buffer);
        }
    }

    [DllImport("libc.so.6", EntryPoint = "statfs", SetLastError = true)]
    private static extern int LinuxStatfs([MarshalAs(UnmanagedType.LPUTF8Str)] string path, byte[] buffer);

    [DllImport("libc.so.6", EntryPoint = "statx", SetLastError = true)]
    private static extern int LinuxStatx(int directoryFd, [MarshalAs(UnmanagedType.LPUTF8Str)] string path, int flags, uint mask, byte[] buffer);

    [DllImport("libc", EntryPoint = "stat$INODE64", SetLastError = true)]
    private static extern int MacStat([MarshalAs(UnmanagedType.LPUTF8Str)] string path, byte[] buffer);

    [DllImport("libc", EntryPoint = "stat", SetLastError = true)]
    private static extern int MacStatPlain([MarshalAs(UnmanagedType.LPUTF8Str)] string path, byte[] buffer);
}
