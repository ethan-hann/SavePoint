using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace Savepoint.Core.Git;

public sealed partial class GitEngine
{
    // The last answer for each git directory, with a digest of the refs it was worked out from. A snapshot, a removal, a restore or a
    // Free up space run changes a ref, so the digest is the whole test for "the store changed".
    private sealed record CachedEstimate(string Digest, long Bytes);

    /// <summary>
    /// About how many bytes of the repository's stored data would be dropped for good if these refs were deleted and the repository
    /// were then reclaimed: the data that only they reach. Data that any other ref, HEAD or the index also reaches is never counted. The
    /// figure is the data's size as stored (compressed, and as a delta where it is one), so it is close to the space that comes back
    /// but not exact. Only refs that <see cref="ClearExactAsync"/> would delete count: a ref that is gone, or holds another value than
    /// the one given, stays and holds its data. Reads only the git directory and never reads an object's content. The answer is kept
    /// until a ref, HEAD or the index of the repository changes, so asking again for the same refs costs one listing of the refs.
    /// </summary>
    /// <param name="repo">The folder whose repository holds the refs.</param>
    /// <param name="doomed">The refs and the values they held when they were found, as <see cref="FindClearableAsync"/> returns them.</param>
    /// <param name="ct">Cancels the call and stops the git it has running.</param>
    /// <returns>The estimate in bytes; zero when the repository does not exist or nothing is dropped.</returns>
    /// <exception cref="OperationCanceledException">The token was cancelled.</exception>
    /// <exception cref="Savepoint.Core.Errors.GitOperationException">git failed.</exception>
    public async Task<long> EstimateFreedBytesAsync(RepoRef repo, IReadOnlyCollection<ClearedRef> doomed, CancellationToken ct = default)
    {
        repo = Normalize(repo);
        ArgumentNullException.ThrowIfNull(doomed);
        ct.ThrowIfCancellationRequested();
        if (doomed.Count == 0 || !HasRepository(repo))
        {
            return 0;
        }

        var present = await ReadEveryRefAsync(repo, ct).ConfigureAwait(false);
        var head = ReadHeadBranch(repo);
        var going = doomed
            .DistinctBy(item => item.RefName, StringComparer.Ordinal)
            .Where(item => present.TryGetValue(item.RefName, out var value) && string.Equals(value, item.Value, StringComparison.Ordinal) && IsClearable(item.RefName, head))
            .ToList();
        if (going.Count == 0)
        {
            return 0;
        }

        var headId = await ResolveHeadAsync(repo, ct).ConfigureAwait(false);
        var digest = DigestOf(repo, present, headId, going);
        if (_estimates.TryGetValue(repo.GitDir, out var cached) && cached.Digest == digest)
        {
            return cached.Bytes;
        }

        var goneNames = going.Select(item => item.RefName).ToHashSet(StringComparer.Ordinal);
        var kept = present.Where(entry => !goneNames.Contains(entry.Key)).Select(entry => entry.Value).Append(headId).OfType<string>().Distinct(StringComparer.Ordinal).ToList();
        var bytes = await MeasureAsync(repo, [.. going.Select(item => item.Value).Distinct(StringComparer.Ordinal)], kept, ct).ConfigureAwait(false);
        _estimates[repo.GitDir] = new CachedEstimate(digest, bytes);
        return bytes;
    }

    // What the tips reach that nothing in "kept" (nor the index) reaches, as stored sizes. Two steps keep it fast and right. Git's own
    // "tips not kept" listing is cheap and never leaves out anything that is only the tips', but it can list data an older kept snapshot
    // holds, because it subtracts only the trees at the edge of the history it walked. So it gives the candidates, and a pass over
    // everything kept (the same walk a reclaim makes to find what is unreachable) takes out the ones a kept snapshot reaches.
    private async Task<long> MeasureAsync(RepoRef repo, IReadOnlyList<string> tips, IReadOnlyList<string> kept, CancellationToken ct)
    {
        var candidates = new HashSet<string>(StringComparer.Ordinal);
        var wanted = string.Concat(tips.Select(tip => tip + "\n").Concat(kept.Select(id => "^" + id + "\n")));
        await StreamNamesAsync(repo, ["rev-list", "--objects", "--stdin"], ct, name => candidates.Add(name), wanted).ConfigureAwait(false);
        if (candidates.Count == 0)
        {
            return 0;
        }

        // Runs even with nothing kept: the index can still name data, and "--stdin" keeps git from refusing a walk with no revisions.
        await StreamNamesAsync(repo, ["rev-list", "--objects", "--indexed-objects", "--stdin"], ct, name => candidates.Remove(name), string.Concat(kept.Select(id => id + "\n"))).ConfigureAwait(false);
        if (candidates.Count == 0)
        {
            return 0;
        }

        // The size as stored: a loose object's file, or what a pack holds for it. No content is read.
        long total = 0;
        void Add(string line)
        {
            if (long.TryParse(line.AsSpan().Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var size))
            {
                total += size;
            }
        }

        await RunAsync(GitDirOnly(repo, "cat-file", "--batch-check=%(objectsize:disk)") with { Stdin = string.Concat(candidates.Select(name => name + "\n")), OnStdoutLine = Add }, ct).ConfigureAwait(false);
        return total;
    }

    // Every ref of the repository with the value it holds.
    private async Task<Dictionary<string, string>> ReadEveryRefAsync(RepoRef repo, CancellationToken ct)
    {
        var listed = await RunAsync(GitDirOnly(repo, "for-each-ref", "--format=%(refname) %(objectname)"), ct).ConfigureAwait(false);
        var present = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in listed.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (line.LastIndexOf(' ') is > 0 and var split)
            {
                present[line[..split]] = line[(split + 1)..];
            }
        }

        return present;
    }

    // What HEAD points at, or null in a repository with no commit yet.
    private async Task<string?> ResolveHeadAsync(RepoRef repo, CancellationToken ct)
    {
        var result = await RunAsync(GitDirOnly(repo, "rev-parse", "--verify", "-q", "HEAD") with { AllowedExitCodes = [0, 1] }, ct).ConfigureAwait(false);
        return result.ExitCode == 0 ? result.StandardOutput.Trim() : null;
    }

    // The refs, HEAD and the doomed set, plus the index file's size and time: what the index names is kept too, and a switch that finishes
    // later changes it with no ref moving. The packs are in it too, since a repack moves no ref yet changes the size the same data takes.
    private static string DigestOf(RepoRef repo, Dictionary<string, string> refs, string? headId, IReadOnlyList<ClearedRef> going)
    {
        var text = new StringBuilder();
        var index = new FileInfo(Path.Combine(repo.GitDir, "index"));
        text.Append("index ").Append(index.Exists ? index.Length : -1).Append(' ').Append(index.Exists ? index.LastWriteTimeUtc.Ticks : 0).Append('\n');
        var packs = new DirectoryInfo(Path.Combine(repo.GitDir, "objects", "pack"));
        if (packs.Exists)
        {
            foreach (var pack in packs.EnumerateFiles("*.pack").OrderBy(file => file.Name, StringComparer.Ordinal))
            {
                text.Append("pack ").Append(pack.Name).Append(' ').Append(pack.Length).Append(' ').Append(pack.LastWriteTimeUtc.Ticks).Append('\n');
            }
        }
        foreach (var entry in refs.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            text.Append(entry.Key).Append(' ').Append(entry.Value).Append('\n');
        }

        text.Append("HEAD ").Append(headId).Append('\n');
        foreach (var item in going.OrderBy(item => item.RefName, StringComparer.Ordinal))
        {
            text.Append("- ").Append(item.RefName).Append('\n');
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));
    }
}
