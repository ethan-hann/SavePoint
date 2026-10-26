using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Savepoint.Core.Platform;

namespace Savepoint.Core.Paths;

// The Windows calls that say what a path really is. Every call opens a handle with at most attribute rights and full sharing,
// so a folder in use is never disturbed, and with backup semantics so folders open.
internal static class WindowsFiles
{
    // Which volume name the final path is asked for: the drive letter form, or the volume GUID form.
    internal enum VolumeNameKind
    {
        Dos,
        Guid,
    }

    private const uint _noAccess = 0;
    private const uint _readAttributes = 0x80;
    private const uint _listDirectory = 0x1;
    private const uint _shareReadWrite = 0x3;
    private const uint _shareAll = 0x7;
    private const uint _openExisting = 3;
    private const uint _backupSemantics = 0x02000000;
    private const int _fileIdInfoClass = 18;
    private const uint _volumeNameDos = 0;
    private const uint _volumeNameGuid = 1;
    private const int _errorMoreData = 234;
    private const string _volumePrefix = @"\\?\Volume{";

    public static FileIdentity? ReadIdentity(string path)
    {
        using var handle = Open(path, _readAttributes);
        if (handle.IsInvalid)
        {
            return null;
        }

        if (GetFileInformationByHandleEx(handle, _fileIdInfoClass, out var info, (uint)Marshal.SizeOf<FileIdInfo>()))
        {
            return new FileIdentity(info.VolumeSerialNumber, new UInt128(info.FileIdHigh, info.FileIdLow));
        }

        return ReadLegacyIdentity(handle);
    }

    // Volumes without 128-bit ids (FAT, some network shares) still report a 64-bit index.
    internal static FileIdentity? ReadLegacyIdentity(string path)
    {
        using var handle = Open(path, _readAttributes);
        return handle.IsInvalid ? null : ReadLegacyIdentity(handle);
    }

    private static FileIdentity? ReadLegacyIdentity(SafeFileHandle handle) =>
        GetFileInformationByHandle(handle, out var legacy)
            ? new FileIdentity(legacy.VolumeSerialNumber, new UInt128(0, ((ulong)legacy.FileIndexHigh << 32) | legacy.FileIndexLow))
            : null;

    // A handle that keeps the folder from being renamed, moved or deleted for as long as it is open. Reading the folder's list counts in
    // sharing checks (attribute-only access does not), and leaving delete out of the share mode is what refuses a rename. Everything
    // else, git working inside the folder included, is unaffected. Null when the folder cannot be opened.
    public static SafeFileHandle? HoldFolder(string path)
    {
        var handle = CreateFile(ExtendedLength(path), _listDirectory, _shareReadWrite, IntPtr.Zero, _openExisting, _backupSemantics, IntPtr.Zero);
        if (handle.IsInvalid)
        {
            handle.Dispose();
            return null;
        }

        return handle;
    }

    public static IReadOnlyList<string> ReadFinalPaths(string path)
    {
        using var handle = Open(path, _noAccess);
        return handle.IsInvalid
            ? []
            : NamesOf(kind => FinalPath(handle, kind == VolumeNameKind.Dos ? _volumeNameDos : _volumeNameGuid));
    }

    // A drive letter names the volume when there is one; a volume mounted only in folders is named by GUID and mapped back.
    internal static IReadOnlyList<string> NamesOf(Func<VolumeNameKind, string?> finalPath)
    {
        if (finalPath(VolumeNameKind.Dos) is { } dos)
        {
            return WithoutPrefix(dos) is { } name ? [name] : [];
        }

        return finalPath(VolumeNameKind.Guid) is { } guid ? MountedAs(guid) : [];
    }

    // \\?\Volume{guid}\dir\file becomes one name per folder the volume is mounted in; none when it is mounted nowhere.
    internal static IReadOnlyList<string> MountedAs(string guidPath)
    {
        if (!guidPath.StartsWith(_volumePrefix, StringComparison.OrdinalIgnoreCase) || guidPath.IndexOf('}') is not (>= 0 and var close))
        {
            return [];
        }

        var volume = guidPath[..(close + 1)] + @"\";
        var rest = guidPath[(close + 1)..].TrimStart('\\');
        var buffer = new char[260];
        if (!GetVolumePathNamesForVolumeName(volume, buffer, (uint)buffer.Length, out var needed))
        {
            if (Marshal.GetLastWin32Error() != _errorMoreData)
            {
                return [];
            }

            buffer = new char[needed];
            if (!GetVolumePathNamesForVolumeName(volume, buffer, (uint)buffer.Length, out _))
            {
                return [];
            }
        }

        return [.. new string(buffer).Split('\0', StringSplitOptions.RemoveEmptyEntries).Select(mount => mount.TrimEnd('\\') + @"\" + rest)];
    }

    private static string? FinalPath(SafeFileHandle handle, uint volumeFlag)
    {
        var buffer = new char[512];
        var length = GetFinalPathNameByHandle(handle, buffer, (uint)buffer.Length, volumeFlag);
        if (length > buffer.Length)
        {
            buffer = new char[length];
            length = GetFinalPathNameByHandle(handle, buffer, (uint)buffer.Length, volumeFlag);
        }

        return length == 0 || length > buffer.Length ? null : new string(buffer, 0, (int)length);
    }

    // The API writes \\?\C:\dir and \\?\UNC\server\share\dir; only a drive or share path is a name the rest of the code understands.
    private static string? WithoutPrefix(string finalPath)
    {
        var name = finalPath.StartsWith(@"\\?\UNC\", StringComparison.OrdinalIgnoreCase)
            ? @"\\" + finalPath[@"\\?\UNC\".Length..]
            : finalPath.StartsWith(@"\\?\", StringComparison.Ordinal) ? finalPath[4..] : finalPath;
        return PathSyntax.For(OsKind.Windows).IsAbsolute(name) ? name : null;
    }

    private static SafeFileHandle Open(string path, uint access) =>
        CreateFile(ExtendedLength(path), access, _shareAll, IntPtr.Zero, _openExisting, _backupSemantics, IntPtr.Zero);

    // The \\?\ form skips path normalization and the 260-character limit; the path arrives already normalized.
    private static string ExtendedLength(string path)
    {
        if (path.StartsWith(@"\\?\", StringComparison.Ordinal))
        {
            return path;
        }

        return path.StartsWith(@"\\", StringComparison.Ordinal) ? @"\\?\UNC\" + path[2..] : @"\\?\" + path;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "CreateFileW")]
    private static extern SafeFileHandle CreateFile(
        string fileName,
        uint desiredAccess,
        uint shareMode,
        IntPtr securityAttributes,
        uint creationDisposition,
        uint flagsAndAttributes,
        IntPtr templateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandleEx(SafeFileHandle file, int informationClass, out FileIdInfo information, uint bufferSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileInformationByHandle(SafeFileHandle file, out ByHandleFileInformation information);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "GetFinalPathNameByHandleW")]
    private static extern uint GetFinalPathNameByHandle(SafeFileHandle file, char[] filePath, uint bufferLength, uint flags);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "GetVolumePathNamesForVolumeNameW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumePathNamesForVolumeName(string volumeName, char[] volumePathNames, uint bufferLength, out uint returnLength);

    // FILE_ID_INFO: the volume serial and a 128-bit id, little-endian.
    [StructLayout(LayoutKind.Sequential)]
    private struct FileIdInfo
    {
        public ulong VolumeSerialNumber;
        public ulong FileIdLow;
        public ulong FileIdHigh;
    }

    // Pack 4 because the FILETIME fields are two 32-bit halves, so the times start on a 4-byte boundary and not an 8-byte one.
    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    private struct ByHandleFileInformation
    {
        public uint FileAttributes;
        public ulong CreationTime;
        public ulong LastAccessTime;
        public ulong LastWriteTime;
        public uint VolumeSerialNumber;
        public uint FileSizeHigh;
        public uint FileSizeLow;
        public uint NumberOfLinks;
        public uint FileIndexHigh;
        public uint FileIndexLow;
    }
}
