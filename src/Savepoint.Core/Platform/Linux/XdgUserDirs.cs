using Savepoint.Core.Paths;

namespace Savepoint.Core.Platform.Linux;

/// <summary>Reads the XDG_*_DIR lines of ~/.config/user-dirs.dirs (a shell-style KEY="value" file).</summary>
internal static class XdgUserDirs
{
    /// <summary>A folder set to the home directory itself is how xdg-user-dirs marks it turned off, so it is left out.</summary>
    public static IReadOnlyDictionary<string, string> Parse(string? content, string home, PathSyntax syntax)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (content is null)
        {
            return result;
        }

        foreach (var rawLine in content.Split('\n'))
        {
            var line = rawLine.Trim();
            var equals = line.IndexOf('=');
            if (line.StartsWith('#') || equals <= 0)
            {
                continue;
            }

            var key = line[..equals].Trim();
            var value = line[(equals + 1)..].Trim();
            if (value.Length >= 2 && value[0] is '"' or '\'' && value[^1] == value[0])
            {
                value = value[1..^1];
            }

            if (value.StartsWith("$HOME", StringComparison.Ordinal))
            {
                value = home + value["$HOME".Length..];
            }

            if (!syntax.IsAbsolute(value))
            {
                continue;
            }

            var normalized = syntax.Normalize(value);
            if (!string.Equals(normalized, home, syntax.Comparison))
            {
                result[key] = normalized;
            }
        }

        return result;
    }
}
