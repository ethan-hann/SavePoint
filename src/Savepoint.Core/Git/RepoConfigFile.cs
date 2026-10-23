using System.Text;
using System.Text.RegularExpressions;
using Savepoint.Core.Errors;

namespace Savepoint.Core.Git;

// The settings every SavePoint repository carries, kept in one delimited block at the end of the repository's
// config file. Writing the file directly costs no git process, and git reads the block like any other settings.
internal static partial class RepoConfigFile
{
    // Bumped whenever a setting below changes, so existing repositories take the new value the next time they are touched.
    public const string Version = "5";

    internal const string BlockStart = "# >>> savepoint managed (do not edit) >>>";
    internal const string BlockEnd = "# <<< savepoint managed <<<";

    private const string _stampSection = "savepoint";
    private const string _stampKey = "repoConfigVersion";

    public static string ConfigPath(string gitDir) => Path.Combine(gitDir, "config");

    // Current means the file is exactly what a rewrite of it would write. The stamp alone is not enough: a repository from somewhere
    // else can carry it along with settings that name a program. Costs no git process, and a steady state is a string comparison.
    public static bool IsCurrent(string gitDir) =>
        AtomicText.ReadOrNull(ConfigPath(gitDir)) is { } text && string.Equals(text, Rewrite(text, OperatingSystem.IsWindows()), StringComparison.Ordinal);

    public static void Apply(string gitDir, bool isWindows)
    {
        // Only a missing file starts from nothing: one that cannot be read has settings git wrote (core.ignorecase among them) that a rebuild would lose.
        var path = ConfigPath(gitDir);
        var text = File.Exists(path)
            ? AtomicText.ReadOrNull(path) ?? throw new GitOperationException($"The repository's config file '{path}' could not be read, so it was left as it is.")
            : string.Empty;
        AtomicText.Write(path, Rewrite(text, isWindows));
    }

    internal static string? ReadStamp(string configText)
    {
        string? stamp = null;
        var section = string.Empty;
        foreach (var line in configText.Split('\n'))
        {
            if (SectionLine().Match(line) is { Success: true } header)
            {
                section = header.Groups["name"].Value;
            }
            else if (KeyLine().Match(line) is { Success: true } entry
                && section.Equals(_stampSection, StringComparison.OrdinalIgnoreCase)
                && entry.Groups["key"].Value.Equals(_stampKey, StringComparison.OrdinalIgnoreCase))
            {
                stamp = entry.Groups["value"].Value;
            }
        }

        return stamp;
    }

    // The settings git init writes, which a repository needs and which name no program. Every other setting in a config file is
    // dropped: a repository from somewhere else can carry settings that make git run a program (core.fsmonitor, an alias, a filter,
    // an include of another file), and SavePoint needs none of them.
    private static readonly (string Section, string Key)[] _initSettings =
    [
        ("core", "repositoryformatversion"),
        ("core", "filemode"),
        ("core", "bare"),
        ("core", "ignorecase"),
        ("core", "precomposeunicode"),
        ("core", "symlinks"),
        ("extensions", "objectformat"),
        ("extensions", "refstorage"),
    ];

    // Keeps only the settings git init writes (apart from the ones the block sets, and the work tree path, which always comes from the
    // environment), then appends a fresh block. Each kept setting is written again as one plain line.
    internal static string Rewrite(string configText, bool isWindows)
    {
        var lines = configText.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').ToList();
        if (lines.Count > 0 && lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        RemoveBlock(lines);

        var result = new StringBuilder();
        string? current = null;
        foreach (var (section, key, value) in KeptSettings(lines, isWindows))
        {
            if (!string.Equals(current, section, StringComparison.Ordinal))
            {
                result.Append('[').Append(section).Append("]\n");
                current = section;
            }

            result.Append('\t').Append(key).Append(" = ").Append(value).Append('\n');
        }

        result.Append(Block(isWindows));
        return result.ToString();
    }

    internal static IReadOnlyList<(string Section, string Key, string Value)> Settings(bool isWindows)
    {
        List<(string, string, string)> settings =
        [
            ("maintenance", "auto", "false"),
            ("gc", "auto", "0"),
            ("core", "looseCompression", "0"),
            ("core", "untrackedCache", "true"),
            // Permission changes, backup tools and scanners move a file's change time without editing it; size and write time still
            // catch real edits, and trusting the change time would make git re-read (or fail on) every file such a tool touched.
            ("core", "trustCtime", "false"),
            ("index", "version", "4"),
            ("checkout", "workers", "0"),
            ("core", "longPaths", "true"),
            ("core", "quotePath", "false"),
            ("core", "autocrlf", "false"),
            ("core", "bigFileThreshold", "128m"),
            ("core", "logAllRefUpdates", "false"),
        ];

        if (isWindows)
        {
            settings.Add(("core", "symlinks", "false"));
        }

        // Last, so a rewrite that stopped half way is repeated.
        settings.Add((_stampSection, _stampKey, Version));
        return settings;
    }

    private static string Block(bool isWindows)
    {
        var block = new StringBuilder();
        block.Append(BlockStart).Append('\n');
        string? current = null;
        foreach (var (section, key, value) in Settings(isWindows))
        {
            if (!string.Equals(current, section, StringComparison.Ordinal))
            {
                block.Append('[').Append(section).Append("]\n");
                current = section;
            }

            block.Append('\t').Append(key).Append(" = ").Append(value).Append('\n');
        }

        block.Append(BlockEnd).Append('\n');
        return block.ToString();
    }

    private static void RemoveBlock(List<string> lines)
    {
        var start = lines.FindIndex(line => line.Trim() == BlockStart);
        if (start < 0)
        {
            return;
        }

        var end = lines.FindIndex(start, line => line.Trim() == BlockEnd);
        if (end >= 0)
        {
            lines.RemoveRange(start, end - start + 1);
        }
    }

    // The settings git init wrote that stay. A setting defined twice cannot be changed with git config (it refuses to overwrite two
    // values), so the block is the only place ours live and a definition of one outside it goes. A section with a name of its own
    // ([core "x"]) is a different section and never matches.
    private static List<(string Section, string Key, string Value)> KeptSettings(List<string> lines, bool isWindows)
    {
        var ours = Settings(isWindows).Select(setting => (setting.Section, setting.Key)).ToList();
        var kept = new List<(string, string, string)>();
        var section = string.Empty;
        foreach (var line in lines)
        {
            if (SectionLine().Match(line) is { Success: true } header)
            {
                section = header.Groups["sub"].Success ? string.Empty : header.Groups["name"].Value;
            }
            else if (KeyLine().Match(line) is { Success: true } entry && section.Length > 0)
            {
                var key = entry.Groups["key"].Value;
                var value = entry.Groups["value"].Value;
                if (!value.EndsWith('\\')
                    && _initSettings.Any(setting => setting.Section.Equals(section, StringComparison.OrdinalIgnoreCase) && setting.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
                    && !ours.Any(setting => setting.Section.Equals(section, StringComparison.OrdinalIgnoreCase) && setting.Key.Equals(key, StringComparison.OrdinalIgnoreCase)))
                {
                    kept.RemoveAll(setting => setting.Item1 == section.ToLowerInvariant() && setting.Item2 == key.ToLowerInvariant());
                    kept.Add((section.ToLowerInvariant(), key.ToLowerInvariant(), value));
                }
            }
        }

        // Settings of one section sit together; the last definition of a setting is the one git uses.
        return [.. kept.OrderBy(setting => setting.Item1, StringComparer.Ordinal)];
    }

    [GeneratedRegex(@"^\s*\[\s*(?<name>[A-Za-z0-9.\-]+)(?<sub>\s+""[^""]*"")?\s*\]")]
    private static partial Regex SectionLine();

    [GeneratedRegex(@"^\s*(?<key>[A-Za-z][A-Za-z0-9\-]*)\s*=\s*(?<value>[^#;\r]*?)\s*$")]
    private static partial Regex KeyLine();
}
