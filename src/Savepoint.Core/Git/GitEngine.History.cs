using System.Globalization;

namespace Savepoint.Core.Git;

public sealed partial class GitEngine
{
    // Fields are separated by the ASCII unit separator and commits end with NUL (-z), so no subject can break the parse.
    private const string _commitFormat = "--format=%H%x1f%s%x1f%aI%x1f%(trailers:key=Savepoint-Capture,valueonly,separator=%x2c)";

    /// <summary>
    /// Reads the folder's state. Needs the work tree, so it refuses a protected or missing folder and one without this game's marker.
    /// What counts as unsaved is exactly what a capture would record: ignore files inside the game folder are not read, and
    /// files the game's ignore patterns name (those <see cref="RepoRef.IgnorePatterns"/> carries, or the ones the last capture or preview stored) are not changes.
    /// </summary>
    /// <param name="repo">The folder.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <exception cref="Savepoint.Core.Errors.PathSafetyException">The work tree is a protected location.</exception>
    /// <exception cref="Savepoint.Core.Errors.PathMissingException">The work tree does not exist.</exception>
    /// <exception cref="Savepoint.Core.Errors.ConsentMarkerException">The folder has no marker for this game and role.</exception>
    /// <exception cref="Savepoint.Core.Errors.GitOperationException">git failed.</exception>
    public async Task<RepoStatus> GetStatusAsync(RepoRef repo, CancellationToken ct = default)
    {
        repo = Normalize(repo);
        ct.ThrowIfCancellationRequested();
        CheckWorkTree(repo);
        RepoMarker.Verify(repo);

        // HEAD is read from its file, and the changes come from the same listing a capture uses, so what shows as unsaved is exactly what a capture would record.
        var head = GitDirFiles.ReadHead(repo.GitDir);
        var commit = head.IsDetached ? head.DetachedCommit : await ResolveBranchAsync(repo, head.Branch!, ct).ConfigureAwait(false);
        var untracked = ListNewAsync(repo, ct);
        var changed = ListChangedAsync(repo, hasCommits: commit is not null, ct);

        IReadOnlyList<string> changedPaths = await changed.ConfigureAwait(false);
        IReadOnlyList<string> untrackedPaths = (await untracked.ConfigureAwait(false)).Files;
        return new RepoStatus(
            changedPaths.Count == 0 && untrackedPaths.Count == 0,
            head.Branch,
            head.IsDetached,
            commit,
            changedPaths,
            untrackedPaths,
            SwitchJournal.Read(repo.GitDir));
    }

    /// <summary>
    /// Tells a file another program holds open from unsaved work. git reads a file again when its timestamp is too new to
    /// trust, and a file it cannot open then reads as changed, so a status can list a change nobody made.
    /// </summary>
    /// <param name="repo">The folder the status was read from.</param>
    /// <param name="status">The folder's state, from <see cref="GetStatusAsync"/>.</param>
    /// <returns>The full path of a changed file that is held open, when every change in <paramref name="status"/> is such a file; null when there is a new file or a change that can be read, or nothing is held open.</returns>
    public string? FindFileInUse(RepoRef repo, RepoStatus status)
    {
        ArgumentNullException.ThrowIfNull(status);
        return OnlyLockedChanges(Normalize(repo), status.ChangedPaths, status.UntrackedPaths.Count);
    }

    /// <summary>
    /// The points on a branch, newest first. Reads only the git directory, so it works while the game folder is
    /// missing. A branch that does not exist, or a repository that does not exist, has no points.
    /// </summary>
    /// <param name="repo">The folder whose history is read.</param>
    /// <param name="branch">The profile's branch.</param>
    /// <param name="maxCount">Stop after this many points; null reads them all.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="maxCount"/> is not positive.</exception>
    /// <exception cref="Savepoint.Core.Errors.GitOperationException">git failed.</exception>
    public async Task<IReadOnlyList<CommitInfo>> ListCommitsAsync(RepoRef repo, string branch, int? maxCount = null, CancellationToken ct = default)
    {
        repo = Normalize(repo);
        BranchNames.Require(branch);
        if (maxCount is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxCount), maxCount, "A limit must be positive.");
        }

        if (!await BranchHasPointsAsync(repo, branch, ct).ConfigureAwait(false))
        {
            return [];
        }

        List<string> arguments = ["log", "-z", _commitFormat];
        if (maxCount is { } limit)
        {
            arguments.Add("-n");
            arguments.Add(limit.ToString(CultureInfo.InvariantCulture));
        }

        arguments.Add(GitDirFiles.BranchPrefix + branch);
        arguments.Add("--");
        var result = await RunAsync(GitDirOnly(repo, [.. arguments]), ct).ConfigureAwait(false);
        return ParseCommits(result);
    }

    /// <summary>How many points a branch holds; 0 when the branch or the repository does not exist.</summary>
    /// <param name="repo">The folder.</param>
    /// <param name="branch">The profile's branch.</param>
    /// <param name="ct">Cancels the call.</param>
    public async Task<int> CountCommitsAsync(RepoRef repo, string branch, CancellationToken ct = default)
    {
        repo = Normalize(repo);
        BranchNames.Require(branch);
        if (!await BranchHasPointsAsync(repo, branch, ct).ConfigureAwait(false))
        {
            return 0;
        }

        var result = await RunAsync(GitDirOnly(repo, "rev-list", "--count", GitDirFiles.BranchPrefix + branch, "--"), ct).ConfigureAwait(false);
        return int.Parse(result.StandardOutput.Trim(), CultureInfo.InvariantCulture);
    }

    /// <summary>The full id of the newest point on a branch; null when the branch or the repository does not exist.</summary>
    /// <param name="repo">The folder.</param>
    /// <param name="branch">The profile's branch.</param>
    /// <param name="ct">Cancels the call.</param>
    public Task<string?> GetBranchTipAsync(RepoRef repo, string branch, CancellationToken ct = default)
    {
        repo = Normalize(repo);
        BranchNames.Require(branch);
        return HasRepository(repo) ? ResolveBranchAsync(repo, branch, ct) : Task.FromResult<string?>(null);
    }

    /// <summary>True when the branch has at least one point.</summary>
    /// <param name="repo">The folder.</param>
    /// <param name="branch">The profile's branch.</param>
    /// <param name="ct">Cancels the call.</param>
    public async Task<bool> BranchExistsAsync(RepoRef repo, string branch, CancellationToken ct = default) =>
        await GetBranchTipAsync(repo, branch, ct).ConfigureAwait(false) is not null;

    /// <summary>Every branch, sorted by name; empty when the repository does not exist or has no point yet.</summary>
    /// <param name="repo">The folder.</param>
    /// <param name="ct">Cancels the call.</param>
    public async Task<IReadOnlyList<string>> ListBranchesAsync(RepoRef repo, CancellationToken ct = default)
    {
        repo = Normalize(repo);
        if (!HasRepository(repo))
        {
            return [];
        }

        var result = await RunAsync(GitDirOnly(repo, "for-each-ref", "--format=%(refname)", GitDirFiles.BranchPrefix), ct).ConfigureAwait(false);
        return
        [
            .. result.StandardOutput
                .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(name => name.StartsWith(GitDirFiles.BranchPrefix, StringComparison.Ordinal))
                .Select(name => name[GitDirFiles.BranchPrefix.Length..]),
        ];
    }

    /// <summary>
    /// The points that only <paramref name="branch"/> holds: reachable from it and from none of the other branches
    /// (what deleting the profile would lose). Newest first. Other branches that do not exist are ignored.
    /// </summary>
    /// <param name="repo">The folder.</param>
    /// <param name="branch">The branch that would be deleted.</param>
    /// <param name="otherBranches">The branches that stay.</param>
    /// <param name="ct">Cancels the call.</param>
    public async Task<IReadOnlyList<CommitInfo>> ListCommitsOnlyOnAsync(RepoRef repo, string branch, IReadOnlyCollection<string> otherBranches, CancellationToken ct = default)
    {
        repo = Normalize(repo);
        BranchNames.Require(branch);
        ArgumentNullException.ThrowIfNull(otherBranches);
        if (!await BranchHasPointsAsync(repo, branch, ct).ConfigureAwait(false))
        {
            return [];
        }

        var existing = await ListBranchesAsync(repo, ct).ConfigureAwait(false);
        var stay = existing.Where(name => name != branch && otherBranches.Contains(name)).ToArray();

        List<string> arguments = ["log", "-z", _commitFormat, GitDirFiles.BranchPrefix + branch];
        if (stay.Length > 0)
        {
            arguments.Add("--not");
            arguments.AddRange(stay.Select(name => GitDirFiles.BranchPrefix + name));
        }

        arguments.Add("--");
        var result = await RunAsync(GitDirOnly(repo, [.. arguments]), ct).ConfigureAwait(false);
        return ParseCommits(result);
    }

    // A git call that reads only the git directory: it needs no work tree, so it works when the game folder is gone.
    private static GitInvocation GitDirOnly(RepoRef repo, params string[] arguments) =>
        new(arguments) { GitDir = repo.GitDir, WorkingDirectory = repo.GitDir, ReadOnly = true };

    private static bool HasRepository(RepoRef repo) => File.Exists(Path.Combine(repo.GitDir, "HEAD"));

    private async Task<bool> BranchHasPointsAsync(RepoRef repo, string branch, CancellationToken ct) =>
        HasRepository(repo) && await ResolveBranchAsync(repo, branch, ct).ConfigureAwait(false) is not null;

    private static List<CommitInfo> ParseCommits(GitResult result)
    {
        var commits = new List<CommitInfo>();
        foreach (var record in result.SplitStandardOutputOnNul())
        {
            // The id, date and trailer cannot hold the separator, but a subject written by something else can; reading the
            // last two fields from the right keeps such a subject from shifting them.
            var fields = record.Trim('\n', '\r').Split('\x1f');
            if (fields.Length < 4 || !DateTimeOffset.TryParse(fields[^2], CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            {
                continue;
            }

            var captureId = fields[^1].Trim() is { Length: > 0 } trailer ? trailer : null;
            commits.Add(new CommitInfo(fields[0], string.Join('\x1f', fields[1..^2]), date, captureId));
        }

        return commits;
    }
}
