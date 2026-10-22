using System.Text;
using System.Text.RegularExpressions;
using Savepoint.Core.Errors;

namespace Savepoint.Core.Git;

// The settings every SavePoint repository carries, kept in one delimited block at the end of the repository's
// config file. Writing the file directly costs no git process, and git reads the block like any other settings.
internal static partial class RepoConfigFile
{
    // Bumped whenever a setting below changes, so existing repositories take the new value the next time they are touched.
    public const string Version = "3";

    internal const string BlockStart = "# >>> savepoint managed (do not edit) >>>";
    internal const string BlockEnd = "# <<< savepoint managed <<<";

    private const string _stampSection = "savepoint";
    private const string _stampKey = "repoConfigVersion";

    public static string ConfigPath(string gitDir) => Path.Combine(gitDir, "config");

    public static bool IsCurrent(string gitDir) =>
        AtomicText.ReadOrNull(ConfigPath(gitDir)) is { } text && ReadStamp(text) == Version;

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

    // Drops the previous block and every other definition of a setting the block holds (git init writes some, and the work tree path, which always comes from the environment), then appends a fresh block.
    internal static string Rewrite(string configText, bool isWindows)
    {
        var lines = configText.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').ToList();
        if (lines.Count > 0 && lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        RemoveBlock(lines);
        RemoveSettingsOfOurs(lines, isWindows);

        var result = new StringBuilder();
        foreach (var line in lines)
        {
            result.Append(line).Append('\n');
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

    // A setting defined twice cannot be changed with git config (it refuses to overwrite two values), so the block is the only place ours live.
    private static void RemoveSettingsOfOurs(List<string> lines, bool isWindows)
    {
        var ours = Settings(isWindows).Select(setting => (setting.Section, setting.Key)).ToList();
        ours.Add(("core", "worktree"));
        var section = string.Empty;
        var keep = new List<string>(lines.Count);
        foreach (var line in lines)
        {
            if (SectionLine().Match(line) is { Success: true } header)
            {
                section = header.Groups["name"].Value;
            }
            else if (KeyLine().Match(line) is { Success: true } entry
                && ours.Any(setting => setting.Section.Equals(section, StringComparison.OrdinalIgnoreCase)
                    && setting.Key.Equals(entry.Groups["key"].Value, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }

            keep.Add(line);
        }

        lines.Clear();
        lines.AddRange(keep);
    }

    [GeneratedRegex(@"^\s*\[\s*(?<name>[A-Za-z0-9.\-]+)(\s+""[^""]*"")?\s*\]")]
    private static partial Regex SectionLine();

    [GeneratedRegex(@"^\s*(?<key>[A-Za-z][A-Za-z0-9\-]*)\s*=\s*(?<value>[^#;\r]*?)\s*$")]
    private static partial Regex KeyLine();
}
