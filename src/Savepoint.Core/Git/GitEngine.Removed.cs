using System.Text;

namespace Savepoint.Core.Git;

public sealed partial class GitEngine
{
    /// <summary>
    /// Lists the commits that left a branch and are kept by a record, newest removal first. A commit that a branch reaches again
    /// (a record left by a stop before the branch moved, or a commit brought back as a branch) is not listed, and neither is a
    /// commit that carries no capture id, which is no snapshot. A commit removed twice is listed once, by its latest removal.
    /// Reads only the git directory, so it works while the game folder is missing.
    /// </summary>
    /// <param name="repo">The folder whose repository is read.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>The removed commits; empty when the repository does not exist or nothing was removed.</returns>
    /// <exception cref="Savepoint.Core.Errors.GitOperationException">git failed.</exception>
    public async Task<IReadOnlyList<RemovedCommit>> ListRemovedAsync(RepoRef repo, CancellationToken ct = default)
    {
        repo = Normalize(repo);
        if (!HasRepository(repo))
        {
            return [];
        }

        var records = await ReadRecordsAsync(repo, ct).ConfigureAwait(false);
        if (records.Count == 0)
        {
            return [];
        }

        // What no branch reaches, among the recorded commits and the commits behind them; a recorded commit not in this answer is on a branch.
        var stdin = string.Concat(records.Select(record => record.Commit + "\n"));
        var unreached = await RunAsync(GitDirOnly(repo, "log", "-z", _commitFormat, "--stdin", "--not", "--branches") with { Stdin = stdin }, ct).ConfigureAwait(false);
        var gone = ParseCommits(unreached).ToDictionary(commit => commit.Id, StringComparer.Ordinal);

        return
        [
            .. records
                .Where(record => gone.TryGetValue(record.Commit, out var commit) && commit.CaptureId is not null)
                .GroupBy(record => record.Commit, StringComparer.Ordinal)
                .Select(group => group.MaxBy(record => record.RemovedAt))
                .Select(record => new RemovedCommit(gone[record.Commit], record.Branch, record.Reason, record.RemovedAt))
                .OrderByDescending(removed => removed.RemovedAt)
                .ThenByDescending(removed => removed.Commit.PointTime),
        ];
    }

    /// <summary>
    /// The commits reachable from the given commits, newest first, each with its name, date and capture id: the history a set of tips
    /// holds together, such as a profile's branch and the commits that were taken off it. A commit named twice is listed once. Reads only
    /// the git directory.
    /// </summary>
    /// <param name="repo">The folder whose repository is read.</param>
    /// <param name="commits">Full commit ids; an empty list, or a repository that does not exist, gives an empty answer.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <exception cref="ArgumentException">An id is not a full commit id.</exception>
    /// <exception cref="Savepoint.Core.Errors.GitOperationException">git failed, or an id names no commit in the repository.</exception>
    public async Task<IReadOnlyList<CommitInfo>> ListCommitsFromAsync(RepoRef repo, IReadOnlyCollection<string> commits, CancellationToken ct = default)
    {
        repo = Normalize(repo);
        ArgumentNullException.ThrowIfNull(commits);
        if (commits.Any(id => !GitDirFiles.IsObjectId(id)))
        {
            throw new ArgumentException("Every commit must be given as a full commit id.", nameof(commits));
        }

        if (commits.Count == 0 || !HasRepository(repo))
        {
            return [];
        }

        var result = await RunAsync(GitDirOnly(repo, "log", "-z", _commitFormat, "--stdin") with { Stdin = string.Concat(commits.Distinct(StringComparer.Ordinal).Select(id => id + "\n")) }, ct).ConfigureAwait(false);
        return ParseCommits(result);
    }

    // Writes the records for commits about to leave <branch>, before the branch moves: a stop between the two leaves a record for a
    // commit still on the branch, which is harmless, and never a commit that is off the branch and unrecorded. One ref transaction, so
    // either every record exists or none does.
    private async Task RecordRemovedAsync(RepoRef repo, string branch, IReadOnlyList<string> commits, RemovalReason reason, DateTimeOffset removedAt, CancellationToken ct)
    {
        if (commits.Count == 0)
        {
            return;
        }

        var transaction = new StringBuilder();
        foreach (var commit in commits)
        {
            transaction.Append("update ").Append(RemovedRecords.RefName(removedAt, reason, branch, commit)).Append(' ').Append(commit).Append('\n');
        }

        await RunWriteAsync(() => Invocation(repo, readOnly: false, "update-ref", "-m", "savepoint: keep removed snapshots", "--stdin") with { Stdin = transaction.ToString() }, ct).ConfigureAwait(false);
    }

    // The commits <newest> reaches that <kept> does not, newest first: what moving a branch from <newest> back to <kept> takes off it.
    private async Task<IReadOnlyList<string>> CommitsBetweenAsync(RepoRef repo, string newest, string kept, CancellationToken ct)
    {
        var result = await RunAsync(GitDirOnly(repo, "rev-list", newest, "--not", kept, "--"), ct).ConfigureAwait(false);
        return result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }

    // Deletes every record in one ref transaction, whatever its shape, so nothing under the prefix outlives a free-up.
    private async Task ClearRecordsAsync(RepoRef repo, CancellationToken ct)
    {
        var listed = await RunAsync(GitDirOnly(repo, "for-each-ref", "--format=%(refname)", RemovedRecords.Prefix), ct).ConfigureAwait(false);
        var names = listed.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (names.Length == 0)
        {
            return;
        }

        var transaction = string.Concat(names.Select(name => "delete " + name + "\n"));
        await RunWriteAsync(() => Invocation(repo, readOnly: false, "update-ref", "-m", "savepoint: free up space", "--stdin") with { Stdin = transaction }, ct).ConfigureAwait(false);
    }

    private async Task<List<RemovedRecords.ParsedRecord>> ReadRecordsAsync(RepoRef repo, CancellationToken ct)
    {
        var result = await RunAsync(GitDirOnly(repo, "for-each-ref", "--format=%(refname)", RemovedRecords.Prefix), ct).ConfigureAwait(false);
        var records = new List<RemovedRecords.ParsedRecord>();
        foreach (var name in result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (RemovedRecords.TryParse(name, out var record))
            {
                records.Add(record);
            }
        }

        return records;
    }
}
