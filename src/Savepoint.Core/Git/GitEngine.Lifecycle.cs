namespace Savepoint.Core.Git;

public sealed partial class GitEngine
{
    /// <summary>
    /// Creates the repository for a folder, writes its consent marker when absent, and applies the repo
    /// configuration, <c>info/exclude</c> and <c>info/attributes</c>. Safe to repeat for the same game and role.
    /// </summary>
    /// <param name="repo">The folder to track.</param>
    /// <param name="replaceCorruptMarker">True to overwrite a marker file that is not a valid marker; false refuses it.</param>
    /// <param name="ct">Cancels the call.</param>
    /// <exception cref="Savepoint.Core.Errors.PathSafetyException">The work tree is a protected location; nothing is written.</exception>
    /// <exception cref="Savepoint.Core.Errors.PathMissingException">The work tree does not exist.</exception>
    /// <exception cref="Savepoint.Core.Errors.ConsentMarkerException">Another game or role holds the folder, or the marker is unreadable and <paramref name="replaceCorruptMarker"/> is false.</exception>
    /// <exception cref="Savepoint.Core.Errors.GitOperationException">git failed.</exception>
    public async Task InitAsync(RepoRef repo, bool replaceCorruptMarker = false, CancellationToken ct = default)
    {
        repo = Normalize(repo);
        ct.ThrowIfCancellationRequested();
        CheckWorkTree(repo);

        var marker = RepoMarker.Assess(repo);
        if (marker == MarkerState.Foreign || (marker == MarkerState.Corrupt && !replaceCorruptMarker))
        {
            RepoMarker.Verify(repo);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(repo.GitDir)!);
        if (!IsInitialized(repo.GitDir))
        {
            await RunAsync(Invocation(repo, readOnly: false, "init", "--quiet"), ct).ConfigureAwait(false);
        }

        ct.ThrowIfCancellationRequested();
        EnsureRepoFiles(repo);
        if (marker is MarkerState.Missing or MarkerState.Corrupt)
        {
            RepoMarker.Write(repo, _options.Clock.GetUtcNow());
        }
    }

    /// <summary>
    /// Deletes the git directory, then the consent marker, so an interrupted delete can be repeated. A missing
    /// marker is fine; a marker that belongs to another game or role refuses the delete and removes nothing.
    /// </summary>
    /// <param name="repo">The folder whose history is deleted. The work tree's files are never touched.</param>
    /// <param name="ct">Cancels the call between steps.</param>
    /// <exception cref="Savepoint.Core.Errors.PathSafetyException">The work tree is a protected location.</exception>
    /// <exception cref="Savepoint.Core.Errors.ConsentMarkerException">Another game or role holds the folder.</exception>
    /// <exception cref="Savepoint.Core.Errors.GitOperationException">The git directory could not be removed, or is not a git directory.</exception>
    public async Task DeleteRepoAsync(RepoRef repo, CancellationToken ct = default)
    {
        repo = Normalize(repo);
        ct.ThrowIfCancellationRequested();
        _pathSafety.Check(repo.WorkTree);

        if (RepoMarker.Assess(repo) == MarkerState.Foreign)
        {
            RepoMarker.Verify(repo);
        }

        await GitDirRemoval.RemoveAsync(repo.GitDir, _options.DeleteRetryDelays, ct).ConfigureAwait(false);
        RepoMarker.RemoveIfOurs(repo);
    }

    /// <summary>Reads the folder's consent marker. Never throws: anything unreadable or foreign is "not confirmed".</summary>
    /// <param name="repo">The folder and the game and role it should belong to.</param>
    /// <returns>Confirmed only when the marker names exactly that game and role.</returns>
    public RepoConsent GetConsentStatus(RepoRef repo)
    {
        try
        {
            return Path.IsPathFullyQualified(repo.WorkTree) ? RepoMarker.Consent(repo) : new RepoConsent(false, null);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return new RepoConsent(false, null);
        }
    }

    // Rewrites info/exclude with the game's patterns; the marker pattern always comes last so nothing can re-include it.
    internal Task WriteExcludeAsync(RepoRef repo, IReadOnlyCollection<string> patterns, CancellationToken ct)
    {
        repo = Normalize(repo);
        ct.ThrowIfCancellationRequested();
        RepoIgnoreFiles.WriteExclude(repo.GitDir, patterns);
        return Task.CompletedTask;
    }

    // The settings and rule files a repository must have before anything is written to it; cheap when they are already current.
    private void EnsureRepoFiles(RepoRef repo)
    {
        if (!RepoConfigFile.IsCurrent(repo.GitDir))
        {
            RepoConfigFile.Apply(repo.GitDir, OperatingSystem.IsWindows());
        }

        RepoIgnoreFiles.WriteAttributes(repo.GitDir);
        if (AtomicTextContainsMarkerPattern(repo.GitDir))
        {
            return;
        }

        RepoIgnoreFiles.WriteExclude(repo.GitDir, []);
    }

    private static bool AtomicTextContainsMarkerPattern(string gitDir) =>
        AtomicText.ReadOrNull(RepoIgnoreFiles.ExcludePath(gitDir)) is { } text
        && text.Split('\n').Any(line => line.Trim() == "/" + Paths.ConsentMarker.ExcludePattern);

    private static bool IsInitialized(string gitDir) =>
        File.Exists(Path.Combine(gitDir, "HEAD"))
        && File.Exists(Path.Combine(gitDir, "config"))
        && Directory.Exists(Path.Combine(gitDir, "objects"))
        && Directory.Exists(Path.Combine(gitDir, "refs"));
}
