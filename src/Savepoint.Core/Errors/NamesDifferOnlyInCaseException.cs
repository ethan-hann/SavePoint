using Savepoint.Core.Catalog;

namespace Savepoint.Core.Errors;

/// <summary>
/// A point holds two names that differ only in upper and lower case (<c>Mods/Foo</c> and <c>Mods/foo</c>), and the folder is on a drive
/// that treats them as one name (Windows, macOS, or an NTFS or exFAT drive mounted on Linux), so writing both would put them in the same
/// place (<c>error.launch.caseClash</c>). Nothing was written. Moving the game to a drive that tells them apart, or choosing another
/// profile, is the way out. Args: <c>role</c>, <c>path</c> (the first spelling, absolute, shown to the player), <c>name</c> (its last
/// part) and <c>other</c> (the last part of the second spelling).
/// </summary>
public sealed class NamesDifferOnlyInCaseException : SavepointException
{
    /// <summary>Creates the error.</summary>
    /// <param name="role">Which of the game's folders is on the drive.</param>
    /// <param name="first">The first spelling, absolute.</param>
    /// <param name="second">The second spelling, absolute; it differs from <paramref name="first"/> only in the case of its last part.</param>
    public NamesDifferOnlyInCaseException(RepoRole role, string first, string second)
        : base(
            "error.launch.caseClash",
            $"The {ErrorArgs.NameOf(role)} folder is on a drive that ignores case, and the point holds both '{first}' and '{second}'.",
            new Dictionary<string, object?>
            {
                ["role"] = role,
                ["path"] = first,
                ["name"] = Path.GetFileName(first),
                ["other"] = Path.GetFileName(second),
            })
    {
    }
}
