namespace Savepoint.Core.Catalog;

/// <summary>
/// The kinds of file a game or its mod loader writes while it runs and that are never part of the game or a mod: logs and crash dumps.
/// New games leave them out by default, and the after-play dialog uses the same rules to tell a changed log from a changed game.
/// </summary>
public static class RunTimeOutput
{
    // Only extensions that a measured set of installed games and mod loaders never used for content. Folder names (crash, cache, logs)
    // are left out on purpose: shipped engine tools and shader caches live in folders with those names.
    private static readonly (string Extension, string Rule)[] _kinds =
    [
        (".log", "**/*.log"),
        (".dmp", "**/*.dmp"),
        (".mdmp", "**/*.mdmp"),
    ];

    /// <summary>The leave-out rules a new game starts with, in the order they show.</summary>
    public static IReadOnlyList<string> DefaultRules { get; } = [.. _kinds.Select(kind => kind.Rule)];

    /// <summary>Whether a file's name is one of the run-time output kinds the default rules name.</summary>
    /// <param name="relativePath">The file's path relative to its folder, with <c>/</c> or <c>\</c> separators.</param>
    /// <param name="comparison">How names compare: ignoring case where the game folder's history ignores case, exactly otherwise.</param>
    public static bool IsRunTimeOutput(string relativePath, StringComparison comparison) => RuleFor(relativePath, comparison) is not null;

    /// <summary>
    /// The default rules that cover the given files, in default order, when every file is run-time output; null when any file is
    /// something else or there are no files.
    /// </summary>
    /// <param name="relativePaths">The files, relative to their folder.</param>
    /// <param name="comparison">How names compare.</param>
    public static IReadOnlyList<string>? RulesCovering(IEnumerable<string> relativePaths, StringComparison comparison)
    {
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var path in relativePaths)
        {
            if (RuleFor(path, comparison) is not { } rule)
            {
                return null;
            }

            used.Add(rule);
        }

        return used.Count == 0 ? null : [.. DefaultRules.Where(used.Contains)];
    }

    /// <summary>The existing rules followed by the added ones that are not already there.</summary>
    /// <param name="existing">The game's current rules.</param>
    /// <param name="added">The rules to add.</param>
    public static IReadOnlyList<string> Merge(IReadOnlyList<string> existing, IEnumerable<string> added)
    {
        var merged = new List<string>(existing);
        foreach (var rule in added)
        {
            if (!merged.Contains(rule, StringComparer.Ordinal))
            {
                merged.Add(rule);
            }
        }

        return merged;
    }

    private static string? RuleFor(string relativePath, StringComparison comparison)
    {
        var name = relativePath.AsSpan(relativePath.AsSpan().LastIndexOfAny('/', '\\') + 1);
        foreach (var (extension, rule) in _kinds)
        {
            if (name.Length > extension.Length && name.EndsWith(extension, comparison))
            {
                return rule;
            }
        }

        return null;
    }
}
