using Savepoint.Core.Errors;
using Savepoint.Core.Paths;

namespace Savepoint.Core.Git;

/// <summary>
/// Every git operation SavePoint performs on a tracked folder. Operations that write check the work tree with
/// <see cref="PathSafety"/> and verify the consent marker first; reads run with optional locks off so they never
/// take <c>index.lock</c> while a capture or switch is running.
/// </summary>
/// <remarks>
/// The engine does not serialize callers: the caller holds the game's lock around every operation on one game.
/// Methods that take a <see cref="CancellationToken"/> stop git (and every process it started) when it is
/// cancelled and leave the repository in a state the next call can finish from.
/// </remarks>
public sealed partial class GitEngine
{
    private readonly GitRunner _runner;
    private readonly PathSafety _pathSafety;
    private readonly FreeSpaceProbe _freeSpace;
    private readonly GitEngineOptions _options;

    /// <summary>Builds an engine over a runner.</summary>
    /// <param name="runner">Runs the bundled git.</param>
    /// <param name="pathSafety">Judges every work tree before anything writes to it.</param>
    /// <param name="freeSpace">Reports free bytes per volume; the composition root supplies the real one.</param>
    /// <param name="options">Tuning; null uses the defaults.</param>
    public GitEngine(GitRunner runner, PathSafety pathSafety, FreeSpaceProbe freeSpace, GitEngineOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(pathSafety);
        ArgumentNullException.ThrowIfNull(freeSpace);
        _runner = runner;
        _pathSafety = pathSafety;
        _freeSpace = freeSpace;
        _options = options ?? new GitEngineOptions();
    }

    // Absolute, separator-trimmed paths; a git directory that overlaps the work tree is a caller bug, not a runtime condition.
    private RepoRef Normalize(RepoRef repo)
    {
        ArgumentNullException.ThrowIfNull(repo);
        ArgumentException.ThrowIfNullOrWhiteSpace(repo.GameId);
        if (!Path.IsPathFullyQualified(repo.GitDir) || !Path.IsPathFullyQualified(repo.WorkTree))
        {
            throw new ArgumentException("The git directory and the work tree must be absolute paths.", nameof(repo));
        }

        var normalized = repo with
        {
            GitDir = Path.TrimEndingDirectorySeparator(Path.GetFullPath(repo.GitDir)),
            WorkTree = Path.TrimEndingDirectorySeparator(Path.GetFullPath(repo.WorkTree)),
        };

        if (_pathSafety.PathsOverlap(normalized.GitDir, normalized.WorkTree))
        {
            throw new ArgumentException("The git directory and the work tree must not contain one another.", nameof(repo));
        }

        return normalized;
    }

    // The work tree must be a place SavePoint may track and must exist; nothing has been written when this throws.
    private void CheckWorkTree(RepoRef repo)
    {
        _pathSafety.Check(repo.WorkTree);
        if (!Directory.Exists(repo.WorkTree))
        {
            throw new PathMissingException(repo.WorkTree, repo.Role);
        }
    }

    private GitInvocation Invocation(RepoRef repo, bool readOnly, params string[] arguments) =>
        new(arguments) { GitDir = repo.GitDir, WorkTree = repo.WorkTree, ReadOnly = readOnly };

    private async Task<GitResult> RunAsync(GitInvocation invocation, CancellationToken ct)
    {
        _options.InvocationObserver?.Invoke(invocation);
        try
        {
            return await _runner.RunAsync(invocation, ct).ConfigureAwait(false);
        }
        catch (GitCommandException e)
        {
            throw GitFailure.ToException(e);
        }
    }
}
