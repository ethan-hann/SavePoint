namespace Savepoint.Core.Git;

// Reads `git status --porcelain=v2 -z --branch`. With -z every record ends in NUL and names are raw, so no name is ever quoted.
internal static class PorcelainStatus
{
    public static RepoStatus Parse(IReadOnlyList<string> records)
    {
        string? branch = null;
        string? head = null;
        var changed = new List<string>();
        var untracked = new List<string>();

        foreach (var record in records)
        {
            if (record.StartsWith("# branch.oid ", StringComparison.Ordinal))
            {
                var oid = record["# branch.oid ".Length..];
                head = oid == "(initial)" ? null : oid;
            }
            else if (record.StartsWith("# branch.head ", StringComparison.Ordinal))
            {
                var name = record["# branch.head ".Length..];
                branch = name == "(detached)" ? null : name;
            }
            else if (record.StartsWith("1 ", StringComparison.Ordinal))
            {
                // 1 <XY> <sub> <mH> <mI> <mW> <hH> <hI> <path>: the path may hold spaces, so split off only the first eight fields.
                changed.Add(record.Split(' ', 9)[8]);
            }
            else if (record.StartsWith("u ", StringComparison.Ordinal))
            {
                // u <XY> <sub> <m1> <m2> <m3> <mW> <h1> <h2> <h3> <path>
                changed.Add(record.Split(' ', 11)[10]);
            }
            else if (record.StartsWith("? ", StringComparison.Ordinal))
            {
                untracked.Add(record[2..]);
            }
        }

        var detached = branch is null;
        return new RepoStatus(changed.Count == 0 && untracked.Count == 0, branch, detached, head, changed, untracked);
    }
}
