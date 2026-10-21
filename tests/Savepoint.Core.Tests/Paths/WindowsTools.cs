using System.Diagnostics;
using Savepoint.Core.Paths;

namespace Savepoint.Core.Tests.Paths;

/// <summary>Windows helpers the real-file-system path tests share; every alias they make points into a test's own temp tree.</summary>
internal static class WindowsTools
{
    /// <summary>Maps a free drive letter to <paramref name="path"/> with SUBST, or null when none could be mapped. The caller removes it with <see cref="RemoveSubst"/>.</summary>
    public static string? TrySubst(string path)
    {
        for (var letter = 'Z'; letter >= 'D'; letter--)
        {
            var drive = $"{letter}:";
            if (!Directory.Exists(drive + @"\") && Run("subst", drive, path) == 0)
            {
                return drive;
            }
        }

        return null;
    }

    public static void RemoveSubst(string drive) => Run("subst", drive, "/d");

    public static int Run(string command, params string[] arguments)
    {
        var info = new ProcessStartInfo(command) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        using var process = Process.Start(info)!;
        process.StandardOutput.ReadToEnd();
        process.StandardError.ReadToEnd();
        process.WaitForExit();
        return process.ExitCode;
    }
}

/// <summary>Hides links from name resolution but keeps whatever else the inner probe reports.</summary>
internal sealed class LinkBlindProbe(IPathProbe inner) : IPathProbe
{
    public string? ReadLinkTarget(string path) => null;

    public string? ExpandShortName(string path) => inner.ExpandShortName(path);

    public FileIdentity? ReadIdentity(string path) => inner.ReadIdentity(path);

    public IReadOnlyList<string> ReadFinalPaths(string path) => inner.ReadFinalPaths(path);
}
