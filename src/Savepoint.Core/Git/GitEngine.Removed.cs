using System.Text;
using Savepoint.Core.Errors;

namespace Savepoint.Core.Git;

public sealed partial class GitEngine
{
    /// <summary>
    /// Lists every commit a record keeps, newest removal first, each with whether a branch reaches it again
    /// (<see cref="RemovedCommit.OnBranch"/>: a record left by a stop before the branch moved, or a commit brought back as a branch).
    /// A commit that carries no capture id is no snapshot and is left out. A commit removed twice appears once for each removal.
    /// Reads only the git directory, so it works while the game folder is missing.
    /// </summary>
    /// <param name="repo">The folder whose repository is read.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>The recorded commits; empty when the repository does not exist or nothing was removed.</returns>
    /// <exception cref="Savepoint.Core.Errors.GitOperationException">git failed.</exception>
    public Task<IReadOnlyList<RemovedCommit>> ListRemovedAsync(RepoRef repo, CancellationToken ct = default) => ListRemovedAsync(repo, null, ct);

    /// <summary>
    /// The same list, with the branches that count named: only they decide whether a commit is <see cref="RemovedCommit.OnBranch"/>, so a
    /// branch no profile names cannot take a snapshot out of the list.
    /// </summary>
    /// <param name="repo">The folder whose repository is read.</param>
    /// <param name="countedBranches">The branches that keep a commit off the list, as the catalog's profiles name them; null counts every branch. A name with no branch in this folder is ignored.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <returns>The recorded commits; empty when the repository does not exist or nothing was removed.</returns>
    /// <exception cref="Savepoint.Core.Errors.GitOperationException">git failed.</exception>
    public async Task<IReadOnlyList<RemovedCommit>> ListRemovedAsync(RepoRef repo, IReadOnlyCollection<string>? countedBranches, CancellationToken ct = default)
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

        // Exactly the recorded commits, with no walk behind them.
        var stdin = string.Concat(records.Select(record => record.Commit).Distinct(StringComparer.Ordinal).Select(commit => commit + "\n"));
        var read = await RunAsync(GitDirOnly(repo, "log", "-z", _commitFormat, "--no-walk", "--stdin") with { Stdin = stdin }, ct).ConfigureAwait(false);
        var commits = ParseCommits(read).ToDictionary(commit => commit.Id, StringComparer.Ordinal);

        // What no counted branch reaches, among the recorded commits and the commits behind them; a recorded commit not in this answer is on a branch.
        List<string> counted = countedBranches is null ? ["--branches"] : await CountedBranchRefsAsync(repo, countedBranches, ct).ConfigureAwait(false);
        var gone = records.Select(record => record.Commit).ToHashSet(StringComparer.Ordinal);
        if (counted.Count > 0)
        {
            var unreached = await RunAsync(GitDirOnly(repo, ["rev-list", "--stdin", "--not", .. counted]) with { Stdin = stdin }, ct).ConfigureAwait(false);
            gone = unreached.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(StringComparer.Ordinal);
        }

        return
        [
            .. records
                .Where(record => commits.TryGetValue(record.Commit, out var commit) && commit.CaptureId is not null)
                .Select(record => new RemovedCommit(commits[record.Commit], record.Branch, record.Reason, record.RemovedAt, OnBranch: !gone.Contains(record.Commit), record.RefName))
                .OrderByDescending(removed => removed.RemovedAt)
                .ThenByDescending(removed => removed.Commit.PointTime),
        ];
    }

    // The counted branches that exist here, as refs git can subtract; a name with no branch would make git refuse the whole call.
    private async Task<List<string>> CountedBranchRefsAsync(RepoRef repo, IReadOnlyCollection<string> counted, CancellationToken ct)
    {
        var existing = await ListBranchesAsync(repo, ct).ConfigureAwait(false);
        return [.. existing.Where(counted.Contains).Select(name => GitDirFiles.BranchPrefix + name)];
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

    /// <summary>
    /// Deletes exactly the named records and nothing else, in one ref transaction, so either all of them are gone or none is. A name that
    /// is not a record, or whose record is already gone, is skipped; a record written after the names were read is never touched. The
    /// commits stay in the store until a reclaim drops what no branch or record reaches. Needs only the git directory.
    /// </summary>
    /// <param name="repo">The folder whose repository holds the records.</param>
    /// <param name="records">Record names as <see cref="RemovedCommit.RefName"/> gave them.</param>
    /// <param name="ct">Cancels the call; a cancel before the transaction runs deletes nothing.</param>
    /// <returns>The names of the records that were deleted, which <see cref="RestoreRemovedAsync"/> takes to put them back.</returns>
    /// <exception cref="Savepoint.Core.Errors.OperationCancelledByUserException">The token was cancelled.</exception>
    /// <exception cref="Savepoint.Core.Errors.GitOperationException">git failed.</exception>
    public async Task<IReadOnlyList<string>> ClearRemovedAsync(RepoRef repo, IReadOnlyCollection<string> records, CancellationToken ct = default)
    {
        repo = Normalize(repo);
        ArgumentNullException.ThrowIfNull(records);
        if (records.Count == 0 || !HasRepository(repo))
        {
            return [];
        }

        try
        {
            ct.ThrowIfCancellationRequested();
            using var scope = BeginWrite(repo);
            var listed = await RunAsync(GitDirOnly(repo, "for-each-ref", "--format=%(refname) %(objectname)", RemovedRecords.Prefix), ct).ConfigureAwait(false);
            var present = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var line in listed.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (line.LastIndexOf(' ') is > 0 and var split)
                {
                    present[line[..split]] = line[(split + 1)..];
                }
            }

            var doomed = records
                .Distinct(StringComparer.Ordinal)
                .Where(name => RemovedRecords.TryParse(name, out _) && present.ContainsKey(name))
                .ToList();
            if (doomed.Count == 0)
            {
                return [];
            }

            var transaction = string.Concat(doomed.Select(name => "delete " + name + " " + present[name] + "\n"));
            await RunWriteAsync(() => Invocation(repo, readOnly: false, "update-ref", "-m", "savepoint: free up space", "--stdin") with { Stdin = transaction }, ct).ConfigureAwait(false);
            return doomed;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw new OperationCancelledByUserException(CancelledOperation.Compaction, repo.GameId);
        }
    }

    /// <summary>
    /// Puts back records <see cref="ClearRemovedAsync"/> deleted, in one ref transaction, for a caller that cleared the records of one
    /// folder and then could not clear the other's. The commits are still in the store as long as nothing has been reclaimed since. A name
    /// that is not a record is skipped. Needs only the git directory.
    /// </summary>
    /// <param name="repo">The folder whose repository held the records.</param>
    /// <param name="records">Record names as <see cref="ClearRemovedAsync"/> returned them.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <exception cref="Savepoint.Core.Errors.GitOperationException">git failed, or a commit a record names is no longer in the store.</exception>
    public async Task RestoreRemovedAsync(RepoRef repo, IReadOnlyCollection<string> records, CancellationToken ct = default)
    {
        repo = Normalize(repo);
        ArgumentNullException.ThrowIfNull(records);
        var transaction = new StringBuilder();
        foreach (var name in records.Distinct(StringComparer.Ordinal))
        {
            if (RemovedRecords.TryParse(name, out var record))
            {
                transaction.Append("update ").Append(name).Append(' ').Append(record.Commit).Append('\n');
            }
        }

        if (transaction.Length == 0 || !HasRepository(repo))
        {
            return;
        }

        using var scope = BeginWrite(repo);
        await RunWriteAsync(() => Invocation(repo, readOnly: false, "update-ref", "-m", "savepoint: free up space undone", "--stdin") with { Stdin = transaction.ToString() }, ct).ConfigureAwait(false);
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
