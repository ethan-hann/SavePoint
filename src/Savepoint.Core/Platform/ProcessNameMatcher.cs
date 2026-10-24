namespace Savepoint.Core.Platform;

/// <summary>Decides whether a running process is the game a definition names (used to detect when the game has exited).</summary>
public static class ProcessNameMatcher
{
    // The kernel keeps 15 characters of a process name (TASK_COMM_LEN is 16 with the terminator).
    private const int _linuxCommLength = 15;

    /// <summary>
    /// True when <paramref name="process"/> is the program <paramref name="configuredName"/> names. A trailing .exe is optional on
    /// either side, and on macOS so is a trailing .app. Windows and macOS ignore case; Linux is case-sensitive unless the configured name ends in .exe (a Windows
    /// program under Proton or Wine, whose file name keeps its Windows case rules). macOS and Linux also compare the executable
    /// file's name, since the kernel shortens process names; Linux also compares the first argument (where Proton carries the
    /// Windows path) and, last, a 15-character kernel name against the first 15 characters of the configured one.
    /// </summary>
    /// <param name="configuredName">The process name from the game definition.</param>
    /// <param name="process">A running process.</param>
    /// <param name="os">The OS the process list came from.</param>
    public static bool Matches(string configuredName, RunningProcess process, OsKind os)
    {
        var configured = configuredName.Trim();
        var wanted = WithoutExe(configured);

        // A bundle's name stands for the program inside it, which is what the system lists as the process.
        if (os == OsKind.MacOS && wanted.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
        {
            wanted = wanted[..^4];
        }

        if (wanted.Length == 0)
        {
            return false;
        }

        var comparison = os == OsKind.Linux && !configured.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
            ? StringComparison.Ordinal
            : StringComparison.OrdinalIgnoreCase;

        if (string.Equals(wanted, WithoutExe(process.Name), comparison))
        {
            return true;
        }

        if (os == OsKind.Windows)
        {
            return false;
        }

        if (FileNameMatches(wanted, process.ExecutablePath, comparison))
        {
            return true;
        }

        if (os == OsKind.MacOS)
        {
            return false;
        }

        // Proton games run under a wrapper: the exe name is in the executable path or argv0 (a Windows path), and comm keeps 15 characters of it.
        // When either names a file, that file decides; comm is only a last resort.
        return FileNameMatches(wanted, process.CommandName, comparison)
            || (!HasFileName(process.ExecutablePath) && !HasFileName(process.CommandName) && IsTruncatedComm(process.Name, configured, wanted, comparison));
    }

    private static bool HasFileName(string? path) => !string.IsNullOrEmpty(path);

    private static bool FileNameMatches(string wanted, string? path, StringComparison comparison)
    {
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        return string.Equals(wanted, WithoutExe(path[(path.LastIndexOfAny(['/', '\\']) + 1)..]), comparison);
    }

    private static bool IsTruncatedComm(string comm, string configured, string wanted, StringComparison comparison)
    {
        // The kernel cut the name as the game wrote it, so also try it with the .exe the user may have left off; a 15-character slice only equals a full-length comm.
        foreach (var form in new[] { configured, wanted, wanted + ".exe" })
        {
            if (form.Length >= _linuxCommLength && string.Equals(form[.._linuxCommLength], comm, comparison))
            {
                return true;
            }
        }

        return false;
    }

    private static string WithoutExe(string name) =>
        name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
}
