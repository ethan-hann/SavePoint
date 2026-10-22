using Savepoint.Core.Catalog;

namespace Savepoint.Core.Git;

/// <summary>
/// One tracked folder of one game: the git directory SavePoint keeps under its storage root, and the real
/// game or saves folder git reads and writes. Git reaches both through environment variables; nothing is
/// ever created inside the work tree except the files being tracked and the consent marker.
/// </summary>
/// <param name="GitDir">Absolute path of the git directory. It must not lie inside the work tree or contain it.</param>
/// <param name="WorkTree">Absolute path of the folder being tracked.</param>
/// <param name="GameId">The game that owns the folder; it is written into the consent marker.</param>
/// <param name="Role">Which of the game's two folders this is.</param>
public sealed record RepoRef(string GitDir, string WorkTree, string GameId, RepoRole Role)
{
    /// <summary>
    /// The game's current ignore patterns, in gitignore syntax. When set, every operation on the folder (status, switch, discard,
    /// clean, capture and preview) uses these patterns and brings the repository's <c>info/exclude</c> up to date first, so no
    /// answer depends on an earlier call having written the file. When null, the patterns the repository already holds apply.
    /// </summary>
    public IReadOnlyList<string>? IgnorePatterns { get; init; }
}
