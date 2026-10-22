using System.Globalization;
using Savepoint.Core.Errors;

namespace Savepoint.Core.Git;

public sealed partial class GitEngine
{
    private const string _zeroId = "0000000000000000000000000000000000000000";

    // One path that differs between two trees: its status letter (A, M, D, T) and the blob it held on each side.
    internal readonly record struct TreeChange(char Status, string OldId, string NewId, string Path);

    // Compares two trees without a work tree, so nothing depends on what is on disk.
    private async Task<IReadOnlyList<TreeChange>> DiffTreesAsync(RepoRef repo, string from, string to, CancellationToken ct)
    {
        var result = await RunAsync(GitDirOnly(repo, "diff-tree", "-r", "-z", "--raw", "--no-abbrev", "--no-renames", from, to, "--"), ct).ConfigureAwait(false);
        return ParseTreeChanges(result.SplitStandardOutputOnNul());
    }

    // -z --raw prints ":<old mode> <new mode> <old id> <new id> <status>" and the path as separate NUL-ended records.
    internal static List<TreeChange> ParseTreeChanges(IReadOnlyList<string> records)
    {
        var changes = new List<TreeChange>(records.Count / 2);
        for (var i = 0; i + 1 < records.Count; i += 2)
        {
            var meta = records[i];
            var fields = meta.StartsWith(':') ? meta[1..].Split(' ') : [];
            if (fields.Length < 5)
            {
                throw new GitOperationException($"Unexpected output from git diff-tree: '{meta}'.");
            }

            changes.Add(new TreeChange(fields[4][0], fields[2], fields[3], records[i + 1]));
        }

        return changes;
    }

    // Sizes of stored blobs by id; an id git does not have (a submodule, say) is left out.
    private async Task<Dictionary<string, long>> ObjectSizesAsync(RepoRef repo, IEnumerable<string> ids, CancellationToken ct)
    {
        var wanted = ids.Where(id => id != _zeroId).Distinct(StringComparer.Ordinal).ToArray();
        var sizes = new Dictionary<string, long>(StringComparer.Ordinal);
        if (wanted.Length == 0)
        {
            return sizes;
        }

        var result = await RunAsync(GitDirOnly(repo, "cat-file", "--batch-check") with { Stdin = string.Join('\n', wanted) + "\n" }, ct).ConfigureAwait(false);
        foreach (var line in result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var fields = line.TrimEnd('\r').Split(' ');
            if (fields.Length == 3 && long.TryParse(fields[2], NumberStyles.None, CultureInfo.InvariantCulture, out var size))
            {
                sizes[fields[0]] = size;
            }
        }

        return sizes;
    }
}
