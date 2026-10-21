using Savepoint.Core.Platform;

namespace Savepoint.Core.Paths;

/// <summary>
/// Path rules for one OS, usable on any host: System.IO.Path follows the host, so a Linux table
/// checked from a Windows machine would otherwise be judged by the wrong rules.
/// </summary>
public sealed class PathSyntax
{
    private static readonly PathSyntax _windows = new(OsKind.Windows);
    private static readonly PathSyntax _linux = new(OsKind.Linux);
    private static readonly PathSyntax _macOs = new(OsKind.MacOS);

    private PathSyntax(OsKind os)
    {
        Os = os;
    }

    public OsKind Os { get; }

    public char Separator => IsWindows ? '\\' : '/';

    public StringComparison Comparison => Os == OsKind.Linux ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

    private bool IsWindows => Os == OsKind.Windows;

    public static PathSyntax For(OsKind os) => os switch
    {
        OsKind.Windows => _windows,
        OsKind.Linux => _linux,
        OsKind.MacOS => _macOs,
        _ => throw new ArgumentOutOfRangeException(nameof(os), os, "Unknown OS."),
    };

    public bool IsAbsolute(string path) => TryReadRoot(path, out _, out _);

    public string Normalize(string path)
    {
        var parsed = Split(path, collapseDots: true);
        return Join(parsed.Root, parsed.Parts);
    }

    public string Combine(string basePath, string relative)
    {
        var parsed = Split(basePath, collapseDots: true);
        var parts = new List<string>(parsed.Parts);
        parts.AddRange(SplitRelative(relative));
        return Normalize(Join(parsed.Root, parts));
    }

    internal ParsedPath Split(string absolutePath, bool collapseDots)
    {
        if (!TryReadRoot(absolutePath, out var root, out var rest))
        {
            throw new ArgumentException($"'{absolutePath}' is not an absolute {Os} path.", nameof(absolutePath));
        }

        var parts = new List<string>();
        foreach (var part in SplitRelative(rest))
        {
            if (collapseDots && part == "..")
            {
                if (parts.Count > 0)
                {
                    parts.RemoveAt(parts.Count - 1);
                }
            }
            else
            {
                parts.Add(part);
            }
        }

        return new ParsedPath(root, parts);
    }

    // Drops empty and "." components; ".." stays so a caller walking links can apply it physically.
    internal IEnumerable<string> SplitRelative(string relative)
    {
        var text = IsWindows ? relative.Replace('/', '\\') : relative;
        return text.Split(Separator, StringSplitOptions.RemoveEmptyEntries).Where(p => p != ".");
    }

    internal string Join(string root, IReadOnlyList<string> parts) => root + string.Join(Separator, parts);

    internal bool IsSameOrDescendant(ParsedPath candidate, ParsedPath root)
    {
        if (!string.Equals(candidate.Root, root.Root, Comparison) || candidate.Parts.Count < root.Parts.Count)
        {
            return false;
        }

        for (var i = 0; i < root.Parts.Count; i++)
        {
            if (!string.Equals(candidate.Parts[i], root.Parts[i], Comparison))
            {
                return false;
            }
        }

        return true;
    }

    internal bool IsSame(ParsedPath a, ParsedPath b) =>
        a.Parts.Count == b.Parts.Count && IsSameOrDescendant(a, b);

    private static bool IsDriveLetter(char c) => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z';

    // The root keeps its trailing separator ("C:\", "/", "\\server\share\") so a root-only path joins cleanly.
    private bool TryReadRoot(string path, out string root, out string rest)
    {
        root = rest = string.Empty;
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        if (!IsWindows)
        {
            if (path[0] != '/')
            {
                return false;
            }

            root = "/";
            rest = path[1..];
            return true;
        }

        var text = path.Replace('/', '\\');
        if (text.StartsWith(@"\\?\", StringComparison.Ordinal))
        {
            text = text[4..];
            if (text.StartsWith(@"UNC\", StringComparison.OrdinalIgnoreCase))
            {
                text = @"\\" + text[4..];
            }
        }

        if (text.Length >= 3 && IsDriveLetter(text[0]) && text[1] == ':' && text[2] == '\\')
        {
            root = char.ToUpperInvariant(text[0]) + @":\";
            rest = text[3..];
            return true;
        }

        if (text.StartsWith(@"\\", StringComparison.Ordinal))
        {
            var segments = text[2..].Split('\\', 3);
            if (segments.Length >= 2 && segments[0].Length > 0 && segments[1].Length > 0)
            {
                root = $@"\\{segments[0]}\{segments[1]}\";
                rest = segments.Length == 3 ? segments[2] : string.Empty;
                return true;
            }
        }

        return false;
    }
}

internal sealed record ParsedPath(string Root, IReadOnlyList<string> Parts);
