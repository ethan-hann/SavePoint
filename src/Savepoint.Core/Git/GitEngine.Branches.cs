using System.Text;
using Savepoint.Core.Errors;

namespace Savepoint.Core.Git;

public sealed partial class GitEngine
{
    // The all-zero id tells update-ref the ref must not exist yet.
    private const string _noObject = "0000000000000000000000000000000000000000";

    /// <summary>
    /// Creates a branch at an existing point without touching HEAD or any file.
    /// </summary>
    /// <param name="repo">The folder.</param>
    /// <param name="newBranch">The new branch; it must not exist.</param>
    /// <param name="startPoint">A full commit id, or the name of an existing branch.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <exception cref="ArgumentException">A branch name git would refuse.</exception>
    /// <exception cref="Savepoint.Core.Errors.ConsentMarkerException">The folder has no marker for this game and role.</exception>
    /// <exception cref="Savepoint.Core.Errors.ProfileAlreadyExistsException">The branch exists; nothing was changed.</exception>
    /// <exception cref="Savepoint.Core.Errors.GitOperationException">The start point does not exist, or git failed.</exception>
    public async Task CreateBranchAsync(RepoRef repo, string newBranch, string startPoint, CancellationToken ct = default)
    {
        repo = Normalize(repo);
        BranchNames.Require(newBranch);
        ArgumentException.ThrowIfNullOrWhiteSpace(startPoint);
        ct.ThrowIfCancellationRequested();
        CheckWorkTree(repo);
        RepoMarker.Verify(repo);
        await EnsureRepositoryAsync(repo, ct).ConfigureAwait(false);

        using var scope = BeginWrite(repo);
        var commit = await ResolveStartPointAsync(repo, startPoint, ct).ConfigureAwait(false);
        await CreateBranchRefAsync(repo, newBranch, commit, "savepoint: create branch", ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Creates a branch whose only point has an empty tree (a fresh start for a duplicated profile's saves), without
    /// touching any file. HEAD is left alone unless <paramref name="attachToUnbornHead"/> asks for it. Does nothing when the branch already exists.
    /// </summary>
    /// <param name="repo">The folder.</param>
    /// <param name="branch">The new branch.</param>
    /// <param name="message">The point's name.</param>
    /// <param name="captureId">Written as the point's <c>Savepoint-Capture</c> trailer; null writes none.</param>
    /// <param name="capturedAt">Written as the point's <c>Savepoint-Captured-At</c> trailer when a capture id is given; the engine's clock when null.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <param name="attachToUnbornHead">True to check the new branch out when HEAD is on a branch that has no point yet, as a first capture does, so the folder counts as being on it; HEAD is left alone when it is on a point already.</param>
    /// <exception cref="ArgumentException">A branch name git would refuse, or a blank message.</exception>
    /// <exception cref="Savepoint.Core.Errors.ConsentMarkerException">The folder has no marker for this game and role.</exception>
    /// <exception cref="Savepoint.Core.Errors.GitOperationException">git failed.</exception>
    public async Task CreateEmptyBranchAsync(RepoRef repo, string branch, string message = "Profile started", string? captureId = null, DateTimeOffset? capturedAt = null, CancellationToken ct = default, bool attachToUnbornHead = false)
    {
        repo = Normalize(repo);
        BranchNames.Require(branch);
        var text = CommitMessage.Compose(message, captureId, capturedAt ?? _options.Clock.GetUtcNow());
        ct.ThrowIfCancellationRequested();
        CheckWorkTree(repo);
        RepoMarker.Verify(repo);
        await EnsureRepositoryAsync(repo, ct).ConfigureAwait(false);

        using var scope = BeginWrite(repo);
        if (await ResolveBranchAsync(repo, branch, ct).ConfigureAwait(false) is not null)
        {
            return;
        }

        // Written out, not just known to git, so a pack that has to include the commit's tree always finds it.
        var tree = (await RunWriteAsync(() => Invocation(repo, readOnly: false, "hash-object", "-t", "tree", "-w", "--stdin") with { Stdin = string.Empty }, ct)
            .ConfigureAwait(false)).StandardOutput.Trim();
        var commit = (await RunWriteAsync(() => Invocation(repo, readOnly: false, "commit-tree", tree, "-F", "-") with { Stdin = text }, ct)
            .ConfigureAwait(false)).StandardOutput.Trim();
        await CreateBranchRefAsync(repo, branch, commit, "savepoint: create empty branch", ct).ConfigureAwait(false);

        // Not cancellable, like the other head moves: the branch exists by now.
        if (attachToUnbornHead && GitDirFiles.ReadHead(repo.GitDir) is { Branch: { } current } && await ResolveBranchAsync(repo, current, CancellationToken.None).ConfigureAwait(false) is null)
        {
            await SetHeadAsync(repo, branch, CancellationToken.None).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Deletes a branch; the points only it held become unreachable (a reclaim optimization frees their space). Does
    /// nothing when the branch is already gone, or when the repository is gone or half removed (its marker may be gone
    /// with it). Refuses the branch that is checked out, and a folder another game holds.
    /// </summary>
    /// <param name="repo">The folder.</param>
    /// <param name="branch">The branch to delete.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <exception cref="Savepoint.Core.Errors.ProfileActiveException">The branch is the one checked out.</exception>
    /// <exception cref="Savepoint.Core.Errors.ConsentMarkerException">The folder has no marker for this game and role.</exception>
    /// <exception cref="Savepoint.Core.Errors.GitOperationException">git failed.</exception>
    public async Task DeleteBranchAsync(RepoRef repo, string branch, CancellationToken ct = default)
    {
        repo = Normalize(repo);
        BranchNames.Require(branch);
        ct.ThrowIfCancellationRequested();
        CheckWorkTree(repo);

        // Nothing to delete from when the repository is gone or half removed; its marker may be gone with it.
        if (!IsInitialized(repo.GitDir))
        {
            return;
        }

        RepoMarker.Verify(repo);
        using var scope = BeginWrite(repo);
        if (await ResolveBranchAsync(repo, branch, ct).ConfigureAwait(false) is null)
        {
            return;
        }

        if (string.Equals(GitDirFiles.ReadHead(repo.GitDir).Branch, branch, StringComparison.Ordinal))
        {
            throw new ProfileActiveException(branch);
        }

        await RunWriteAsync(() => Invocation(repo, readOnly: false, "update-ref", "-m", "savepoint: delete branch", "-d", GitDirFiles.BranchPrefix + branch), ct)
            .ConfigureAwait(false);
    }

    // A full commit id as it is, or the tip of the branch of that name.
    private async Task<string> ResolveStartPointAsync(RepoRef repo, string startPoint, CancellationToken ct)
    {
        string? commit;
        if (GitDirFiles.IsObjectId(startPoint))
        {
            var result = await RunAsync(GitDirOnly(repo, "rev-parse", "--verify", "-q", startPoint + "^{commit}") with { AllowedExitCodes = [0, 1] }, ct).ConfigureAwait(false);
            commit = result.ExitCode == 0 ? result.StandardOutput.Trim() : null;
        }
        else
        {
            BranchNames.Require(startPoint);
            commit = await ResolveBranchAsync(repo, startPoint, ct).ConfigureAwait(false);
        }

        return commit ?? throw new GitOperationException($"The start point '{startPoint}' is not a commit or a branch with points.");
    }

    // One ref write: git refuses when the branch is no longer at <expectedOld>, so a branch that moved since it was read is not overwritten.
    private Task MoveBranchRefAsync(RepoRef repo, string branch, string commit, string expectedOld, string reason, CancellationToken ct) =>
        RunWriteAsync(() => Invocation(repo, readOnly: false, "update-ref", "-m", reason, GitDirFiles.BranchPrefix + branch, commit, expectedOld), ct);

    // The all-zero old value makes git refuse atomically when the branch exists, which is how a second creator is told.
    private async Task CreateBranchRefAsync(RepoRef repo, string branch, string commit, string reason, CancellationToken ct)
    {
        try
        {
            await RunWriteAsync(() => Invocation(repo, readOnly: false, "update-ref", "-m", reason, GitDirFiles.BranchPrefix + branch, commit, _noObject), ct)
                .ConfigureAwait(false);
        }
        catch (GitOperationException)
        {
            if (await ResolveBranchAsync(repo, branch, ct).ConfigureAwait(false) is not null)
            {
                throw new ProfileAlreadyExistsException(branch);
            }

            throw;
        }
    }
}

// The text of a commit: the point's name, then the capture id as a trailer in its own paragraph.
internal static class CommitMessage
{
    private const string _captureTrailerKey = "Savepoint-Capture";
    private const string _capturedAtTrailerKey = "Savepoint-Captured-At";

    // What a name may hold. History reads split git's output on the unit separator and take the capture id from a trailer, so
    // a name that holds either could shift the fields of its own point or pass itself off as another capture's id.
    public static void RequireValidMessage(string message, string paramName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(message, paramName);
        if (message.Contains('\0', StringComparison.Ordinal))
        {
            throw new ArgumentException("A message cannot contain a NUL character.", paramName);
        }

        if (message.Contains('\u001f', StringComparison.Ordinal))
        {
            throw new ArgumentException("A message cannot contain the unit separator character.", paramName);
        }

        if (message.Split('\n').Any(line => StartsLikeTrailer(line.TrimEnd('\r'))))
        {
            throw new ArgumentException("A message cannot hold a line that starts like the capture id or capture time trailer.", paramName);
        }
    }

    // git reads the key without regard to case and allows blanks before the colon.
    private static bool StartsLikeTrailer(string line) =>
        IsKeyOf(line, _captureTrailerKey) || IsKeyOf(line, _capturedAtTrailerKey) || IsKeyOf(line, EmptyFolders.TrailerKey);

    private static bool IsKeyOf(string line, string key) =>
        line.StartsWith(key, StringComparison.OrdinalIgnoreCase) && line[key.Length..].TrimStart(' ', '\t').StartsWith(':');

    // The capture time is written with the capture id, as whole milliseconds since 1970, so every commit of one event agrees on it.
    // The empty folders ride in the commit itself so they stay with the point through every operation on history.
    public static string Compose(string message, string? captureId, DateTimeOffset capturedAt, IReadOnlyList<string>? emptyFolders = null)
    {
        RequireValidMessage(message, nameof(message));

        var text = new StringBuilder(message.Trim());
        if (captureId is not null)
        {
            ArgumentException.ThrowIfNullOrEmpty(captureId);
            if (captureId.Any(c => char.IsWhiteSpace(c) || char.IsControl(c)))
            {
                throw new ArgumentException("A capture id cannot contain whitespace or control characters.", nameof(captureId));
            }

            text.Append("\n\nSavepoint-Capture: ").Append(captureId);
            text.Append("\nSavepoint-Captured-At: ").Append(capturedAt.ToUnixTimeMilliseconds().ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        if (emptyFolders is { Count: > 0 })
        {
            text.Append(captureId is null ? "\n\n" : "\n").Append(EmptyFolders.TrailerKey).Append(": ").Append(EmptyFolders.Encode(emptyFolders));
        }

        return text.Append('\n').ToString();
    }
}
