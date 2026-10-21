using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace Savepoint.Core.Paths;

/// <summary>The ready-made probes: the real file system, or none.</summary>
public static class PathProbes
{
    /// <summary>Reads the real file system of the machine the process runs on.</summary>
    public static IPathProbe Host { get; } = new HostPathProbe();

    /// <summary>Sees no links and no identities, so resolution is purely lexical.</summary>
    public static IPathProbe None { get; } = new NoPathProbe();

    private sealed class NoPathProbe : IPathProbe
    {
        public string? ReadLinkTarget(string path) => null;

        public string? ExpandShortName(string path) => null;

        public FileIdentity? ReadIdentity(string path) => null;
    }

    private sealed class HostPathProbe : IPathProbe
    {
        private const uint _readAttributes = 0x80;
        private const uint _shareAll = 0x7;
        private const uint _openExisting = 3;
        private const uint _backupSemantics = 0x02000000;
        private const int _fileIdInfoClass = 18;

        public string? ReadLinkTarget(string path)
        {
            try
            {
                // One attribute read rules out the common case (a plain folder) before any link query.
                var attributes = File.GetAttributes(path);
                if ((attributes & FileAttributes.ReparsePoint) == 0)
                {
                    return null;
                }

                FileSystemInfo info = (attributes & FileAttributes.Directory) != 0 ? new DirectoryInfo(path) : new FileInfo(path);
                return info.LinkTarget;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                return null;
            }
        }

        public string? ExpandShortName(string path)
        {
            if (!OperatingSystem.IsWindows())
            {
                return null;
            }

            var buffer = new char[512];
            var length = GetLongPathName(path, buffer, (uint)buffer.Length);
            if (length > buffer.Length)
            {
                buffer = new char[length];
                length = GetLongPathName(path, buffer, (uint)buffer.Length);
            }

            if (length == 0 || length > buffer.Length)
            {
                return null;
            }

            var longPath = new string(buffer, 0, (int)length);
            return longPath[(longPath.LastIndexOf('\\') + 1)..];
        }

        public FileIdentity? ReadIdentity(string path)
        {
            if (!OperatingSystem.IsWindows())
            {
                return null;
            }

            // Zero rights beyond reading attributes and full sharing, so a folder in use is never disturbed; backup semantics opens folders.
            using var handle = CreateFile(ExtendedLength(path), _readAttributes, _shareAll, IntPtr.Zero, _openExisting, _backupSemantics, IntPtr.Zero);
            if (handle.IsInvalid)
            {
                return null;
            }

            if (GetFileInformationByHandleEx(handle, _fileIdInfoClass, out var info, (uint)Marshal.SizeOf<FileIdInfo>()))
            {
                return new FileIdentity(info.VolumeSerialNumber, new UInt128(info.FileIdHigh, info.FileIdLow));
            }

            // Volumes without 128-bit ids (FAT, some network shares) still report a 64-bit index.
            return GetFileInformationByHandle(handle, out var legacy)
                ? new FileIdentity(legacy.VolumeSerialNumber, new UInt128(0, ((ulong)legacy.FileIndexHigh << 32) | legacy.FileIndexLow))
                : null;
        }

        // The \\?\ form skips path normalization and the 260-character limit; the path arrives already normalized.
        private static string ExtendedLength(string path)
        {
            if (path.StartsWith(@"\\?\", StringComparison.Ordinal))
            {
                return path;
            }

            return path.StartsWith(@"\\", StringComparison.Ordinal) ? @"\\?\UNC\" + path[2..] : @"\\?\" + path;
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "GetLongPathNameW")]
        private static extern uint GetLongPathName(string shortPath, char[] longPath, uint bufferLength);

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

        // FILE_ID_INFO: the volume serial and a 128-bit id, little-endian.
        [StructLayout(LayoutKind.Sequential)]
        private struct FileIdInfo
        {
            public ulong VolumeSerialNumber;
            public ulong FileIdLow;
            public ulong FileIdHigh;
        }

        [StructLayout(LayoutKind.Sequential)]
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
}
