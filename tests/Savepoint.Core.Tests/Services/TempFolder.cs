using System.Diagnostics;

namespace Savepoint.Core.Tests.Services;

// A throwaway folder under the test temp path, removed with everything in it.
internal sealed class TempFolder : IDisposable
{
    public TempFolder(string prefix = "savepoint-svc-")
    {
        Path = Directory.CreateTempSubdirectory(prefix).FullName;
    }

    public string Path { get; }

    public string Combine(params string[] parts) => System.IO.Path.Combine([Path, .. parts]);

    public string WriteBytes(string relativePath, int length)
    {
        var path = Combine(relativePath.Split('/'));
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[length]);
        return path;
    }

    public string WriteBytesWith(string relativePath, byte[] content)
    {
        var path = Combine(relativePath.Split('/'));
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, content);
        return path;
    }

    public string WriteText(string relativePath, string content)
    {
        var path = Combine(relativePath.Split('/'));
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    // A directory link that needs no privilege: a junction on Windows, a symbolic link elsewhere.
    public bool TryCreateDirectoryLink(string linkPath, string targetPath)
    {
        try
        {
            if (!OperatingSystem.IsWindows())
            {
                Directory.CreateSymbolicLink(linkPath, targetPath);
                return true;
            }

            using var mklink = Process.Start(new ProcessStartInfo("cmd.exe", $"/c mklink /J \"{linkPath}\" \"{targetPath}\"")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            })!;
            mklink.StandardOutput.ReadToEnd();
            mklink.WaitForExit();
            return mklink.ExitCode == 0 && Directory.Exists(linkPath);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return false;
        }
    }

    public bool TryCreateFileLink(string linkPath, string targetPath)
    {
        try
        {
            File.CreateSymbolicLink(linkPath, targetPath);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return false;
        }
    }

    public void Dispose()
    {
        try
        {
            RemoveDirectoryLinks(Path);
            Directory.Delete(Path, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
            // Already gone.
        }
    }

    // A link is removed as a link first, so deleting the tree can never walk into its target.
    private static void RemoveDirectoryLinks(string folder)
    {
        foreach (var directory in Directory.EnumerateDirectories(folder))
        {
            if (new DirectoryInfo(directory).Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                Directory.Delete(directory);
            }
            else
            {
                RemoveDirectoryLinks(directory);
            }
        }
    }
}
