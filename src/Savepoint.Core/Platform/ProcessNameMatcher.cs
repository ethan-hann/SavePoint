namespace Savepoint.Core.Platform;

/// <summary>Decides whether a running process is the game a definition names (used to detect when the game has exited).</summary>
public static class ProcessNameMatcher
{
    // The kernel keeps 15 characters of a process name (TASK_COMM_LEN is 16 with the terminator).
    private const int _linuxCommLength = 15;

    public static bool Matches(string configuredName, RunningProcess process, OsKind os)
    {
        var configured = configuredName.Trim();
        var comparison = os == OsKind.Linux ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var wanted = WithoutExe(configured);
        if (wanted.Length == 0)
        {
            return false;
        }

        if (string.Equals(wanted, WithoutExe(process.Name), comparison))
        {
            return true;
        }

        if (os != OsKind.Linux)
        {
            return false;
        }

        // Proton and Wine games run under a wrapper: the real name is in the executable path or argv0
        // (a Windows-style path there), and comm only carries the first 15 characters of it.
        return FileNameMatches(wanted, process.ExecutablePath)
            || FileNameMatches(wanted, process.CommandName)
            || IsTruncatedComm(process.Name, configured, wanted);
    }

    private static bool FileNameMatches(string wanted, string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        return string.Equals(wanted, WithoutExe(path[(path.LastIndexOfAny(['/', '\\']) + 1)..]), StringComparison.Ordinal);
    }

    private static bool IsTruncatedComm(string comm, string configured, string wanted)
    {
        // The kernel cut the full name as the game wrote it, so test it with and without the .exe the user may have left off.
        // Comparing against a 15-character slice means only a full-length comm can match.
        foreach (var form in new[] { configured, wanted, wanted + ".exe" })
        {
            if (form.Length >= _linuxCommLength && string.Equals(form[.._linuxCommLength], comm, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static string WithoutExe(string name) =>
        name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
}
