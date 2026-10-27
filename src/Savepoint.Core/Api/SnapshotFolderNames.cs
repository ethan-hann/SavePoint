namespace Savepoint.Core.Api;

/// <summary>
/// Turns the engine's own folder names into the plain parts a player reads: <c>install</c> for a game's files, <c>mods</c> for its
/// mods folder, <c>saves</c> for its save folders, <c>other</c> for anything else. Job lines carry these words and never the engine's folder names.
/// </summary>
public static class SnapshotFolderNames
{
    /// <summary>The part for a game's files.</summary>
    public const string Install = "install";

    /// <summary>The part for a game's mods folder, when it keeps its mods outside the game's own folder.</summary>
    public const string Mods = "mods";

    /// <summary>The part for a game's save folders.</summary>
    public const string Saves = "saves";

    /// <summary>The part for a folder that is neither.</summary>
    public const string Other = "other";

    /// <summary>The part a folder name stands for.</summary>
    /// <param name="name">A single folder name, such as the name of a git directory in a game's folder.</param>
    public static string PartOf(string name) => name switch
    {
        _ when string.Equals(name, "install.git", StringComparison.OrdinalIgnoreCase) => Install,
        _ when string.Equals(name, "mods.git", StringComparison.OrdinalIgnoreCase) => Mods,
        _ when string.Equals(name, "external.git", StringComparison.OrdinalIgnoreCase) => Saves,
        _ => Other,
    };

    /// <summary>
    /// Describes a path without naming the engine's folder: when the path is, or lies inside, a git directory SavePoint made, the
    /// result is the game's folder that holds it and which part it was; any other path comes back as it is, as <c>other</c>.
    /// </summary>
    /// <param name="path">A path that may lie inside a game's git directory.</param>
    public static (string Path, string Part) Describe(string path)
    {
        var current = path;
        while (Path.GetDirectoryName(current) is { Length: > 0 } parent)
        {
            var part = PartOf(Path.GetFileName(current));
            if (part != Other)
            {
                return (parent, part);
            }

            current = parent;
        }

        return (path, Other);
    }

    /// <summary>
    /// The paths a player is shown for leftovers: each one described by <see cref="Describe"/>, so the engine's folder never
    /// appears, and listed once even when two parts of one game stayed.
    /// </summary>
    /// <param name="paths">The paths that could not be removed.</param>
    public static IReadOnlyList<string> Plain(IEnumerable<string> paths) =>
        [.. paths.Select(path => Describe(path).Path).Distinct(StringComparer.Ordinal)];
}
