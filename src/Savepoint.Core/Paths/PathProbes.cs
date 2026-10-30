using System.Runtime.InteropServices;

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

        public IReadOnlyList<string> ReadFinalPaths(string path) => [];
    }

    private sealed class HostPathProbe : IPathProbe
    {
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
            if (OperatingSystem.IsWindows())
            {
                return WindowsFiles.ReadIdentity(path);
            }

            if (OperatingSystem.IsLinux())
            {
                return UnixFiles.ReadLinuxIdentity(path);
            }

            return OperatingSystem.IsMacOS() ? UnixFiles.ReadMacIdentity(path) : null;
        }

        public IReadOnlyList<string> ReadFinalPaths(string path) => OperatingSystem.IsWindows() ? WindowsFiles.ReadFinalPaths(path) : [];

        public bool KeepsStableIdentity(string path) =>
            !OperatingSystem.IsLinux() || UnixFiles.ReadLinuxFileSystemType(path) is not { } type || UnixFiles.KeepsInodes(type);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "GetLongPathNameW")]
        private static extern uint GetLongPathName(string shortPath, char[] longPath, uint bufferLength);
    }
}
