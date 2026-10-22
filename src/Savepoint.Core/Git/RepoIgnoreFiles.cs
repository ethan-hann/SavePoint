using System.Text;
using Savepoint.Core.Paths;

namespace Savepoint.Core.Git;

// The two rule files that live in the git directory (never in the game folder): what to leave out of a capture,
// and how git must treat file content.
internal static class RepoIgnoreFiles
{
    // Archives and media are already compressed, and the common game containers are large and gain nothing from delta search.
    private static readonly string[] _noDeltaExtensions =
    [
        "zip", "7z", "rar", "gz", "xz", "zst", "cab",
        "mp4", "webm", "mkv", "bik", "bk2", "usm", "ogg", "mp3", "flac",
        "pak", "ucas", "utoc", "bsa", "ba2", "archive", "vpk", "pck",
    ];

    public static string ExcludePath(string gitDir) => Path.Combine(gitDir, "info", "exclude");

    public static string AttributesPath(string gitDir) => Path.Combine(gitDir, "info", "attributes");

    // The lines the exclude file holds besides its comment: the non-empty patterns, then the marker pattern. The marker pattern is
    // last: in a gitignore file the last matching line wins, so no game pattern can re-include the marker.
    public static IReadOnlyList<string> ExcludeLines(IEnumerable<string> patterns)
    {
        var lines = new List<string>();
        foreach (var pattern in patterns)
        {
            if (pattern.Length == 0)
            {
                continue;
            }

            if (pattern.AsSpan().IndexOfAny('\n', '\r', '\0') >= 0)
            {
                throw new ArgumentException("An ignore pattern is one line and cannot contain a line break or NUL.", nameof(patterns));
            }

            lines.Add(pattern);
        }

        lines.Add("/" + ConsentMarker.ExcludePattern);
        return lines;
    }

    public static string ExcludeText(IEnumerable<string> patterns)
    {
        var text = new StringBuilder("# Written by SavePoint before every capture. Changes here are overwritten.\n");
        foreach (var line in ExcludeLines(patterns))
        {
            text.Append(line).Append('\n');
        }

        return text.ToString();
    }

    // These attributes are consulted before any .gitattributes inside the game folder, so nothing there can rewrite stored bytes.
    public static string AttributesText()
    {
        var text = new StringBuilder("# Written by SavePoint. Files are stored and restored byte for byte.\n");
        text.Append("* -text -filter -diff -ident !working-tree-encoding\n");
        foreach (var extension in _noDeltaExtensions)
        {
            text.Append("*.").Append(extension).Append(" -delta\n");
        }

        return text.ToString();
    }

    public static void WriteExclude(string gitDir, IEnumerable<string> patterns) =>
        AtomicText.WriteIfChanged(ExcludePath(gitDir), ExcludeText(patterns));

    public static void WriteAttributes(string gitDir) =>
        AtomicText.WriteIfChanged(AttributesPath(gitDir), AttributesText());
}
