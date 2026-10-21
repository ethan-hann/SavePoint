using System.Diagnostics;

namespace Savepoint.Core.Tests.Git.Engine;

// Folder links for tests: a junction on Windows (no special rights needed), a symbolic link elsewhere.
internal static class TestLinks
{
    public static bool TryCreateFolderLink(string link, string target)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(link)!);
        try
        {
            if (OperatingSystem.IsWindows())
            {
                using var process = Process.Start(new ProcessStartInfo("cmd.exe")
                {
                    ArgumentList = { "/c", "mklink", "/J", link, target },
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                })!;
                process.StandardOutput.ReadToEnd();
                process.WaitForExit();
                return process.ExitCode == 0 && Directory.Exists(link);
            }

            Directory.CreateSymbolicLink(link, target);
            return Directory.Exists(link);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
        {
            return false;
        }
    }

    // Removes only the link, never what it points at.
    public static void RemoveFolderLink(string link)
    {
        if (Directory.Exists(link))
        {
            Directory.Delete(link, recursive: false);
        }
    }
}
