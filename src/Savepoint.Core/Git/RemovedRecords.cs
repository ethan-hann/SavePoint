using System.Globalization;

namespace Savepoint.Core.Git;

// The names of the refs that keep removed commits reachable. Everything a record has to say sits in its name, so one for-each-ref
// answers every question: refs/savepoint/removed/<removed-at ms>/<reason>/<branch, which may hold slashes>/<commit>.
// Outside refs/heads and refs/tags, so no branch listing, history read or delete check ever sees one, and both repack modes keep
// what any ref reaches.
internal static class RemovedRecords
{
    public const string Prefix = "refs/savepoint/removed/";

    private const string _movedBack = "moved-back";
    private const string _replaced = "replaced";

    public static string RefName(DateTimeOffset removedAt, RemovalReason reason, string branch, string commit) =>
        string.Create(CultureInfo.InvariantCulture, $"{Prefix}{removedAt.ToUnixTimeMilliseconds()}/{TokenOf(reason)}/{branch}/{commit}");

    // A ref that is not shaped like a record is ignored, so a stray ref under the prefix can never break a listing.
    public static bool TryParse(string refName, out ParsedRecord record)
    {
        record = default;
        if (!refName.StartsWith(Prefix, StringComparison.Ordinal))
        {
            return false;
        }

        var parts = refName[Prefix.Length..].Split('/');
        if (parts.Length < 4
            || !long.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var milliseconds)
            || ReasonOf(parts[1]) is not { } reason
            || !GitDirFiles.IsObjectId(parts[^1]))
        {
            return false;
        }

        record = new ParsedRecord(refName, parts[^1], string.Join('/', parts[2..^1]), reason, DateTimeOffset.FromUnixTimeMilliseconds(milliseconds));
        return true;
    }

    private static string TokenOf(RemovalReason reason) => reason switch
    {
        RemovalReason.MovedBack => _movedBack,
        RemovalReason.Replaced => _replaced,
        _ => throw new ArgumentOutOfRangeException(nameof(reason), reason, "Unknown removal reason."),
    };

    private static RemovalReason? ReasonOf(string token) => token switch
    {
        _movedBack => RemovalReason.MovedBack,
        _replaced => RemovalReason.Replaced,
        _ => null,
    };

    internal readonly record struct ParsedRecord(string RefName, string Commit, string Branch, RemovalReason Reason, DateTimeOffset RemovedAt);
}
