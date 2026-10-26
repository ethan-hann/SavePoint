using System.Collections.Concurrent;
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
/// cancelled and leave the repository in a state the next call can finish from. Capture and optimization report a
/// cancellation as <see cref="Savepoint.Core.Errors.OperationCancelledByUserException"/>; every other method lets the
/// <see cref="OperationCanceledException"/> through. Operations are asynchronous but do some file work on the calling
/// thread before their first await, and a repository delete runs entirely there, so call them from a background thread.
/// A game folder is never written through a folder link on Windows, a file the game's patterns name is never
/// recorded, overwritten or removed by a switch, and a switch or a discard never deletes a file of the player's that it does not
/// remove itself to make room for a folder or a file of the same name.
/// </remarks>
public sealed partial class GitEngine
{
    private readonly GitRunner _runner;
    private readonly PathSafety _pathSafety;
    private readonly FreeSpaceProbe _freeSpace;
    private readonly string _storageRoot;
    private readonly GitEngineOptions _options;
    private readonly InFlightTable _inFlight = new();

    // The per-question index files that are being read right now; the sweep of stale files leaves them.
    private readonly ConcurrentDictionary<string, byte> _classifying = new(OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    /// <summary>Builds an engine over a runner.</summary>
    /// <param name="runner">Runs the bundled git.</param>
    /// <param name="pathSafety">Judges every work tree before anything writes to it.</param>
    /// <param name="freeSpace">Reports free bytes per volume; the composition root supplies the real one.</param>
    /// <param name="storageRoot">SavePoint's storage folder; a git directory is only ever deleted from inside it.</param>
    /// <param name="options">Tuning; null uses the defaults.</param>
    public GitEngine(GitRunner runner, PathSafety pathSafety, FreeSpaceProbe freeSpace, string storageRoot, GitEngineOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(runner);
        ArgumentNullException.ThrowIfNull(pathSafety);
        ArgumentNullException.ThrowIfNull(freeSpace);
        ArgumentException.ThrowIfNullOrWhiteSpace(storageRoot);
        if (!Path.IsPathFullyQualified(storageRoot))
        {
            throw new ArgumentException("The storage root must be an absolute path.", nameof(storageRoot));
        }

        _runner = runner;
        _pathSafety = pathSafety;
        _freeSpace = freeSpace;
        _storageRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(storageRoot));
        _options = options ?? new GitEngineOptions();
    }

    // A view of another engine that shares everything with it.
    private GitEngine(GitEngine other, bool repairsIndex)
    {
        _runner = other._runner;
        _pathSafety = other._pathSafety;
        _freeSpace = other._freeSpace;
        _storageRoot = other._storageRoot;
        _options = other._options;
        _inFlight = other._inFlight;
        _classifying = other._classifying;
        _repairGate = other._repairGate;
        _indexWritten = other._indexWritten;
        _repairsIndex = repairsIndex;
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

    // Throws when the storage volume cannot take another capture or repack (the floor, plus any bytes the operation is known
    // to need on top); checked before anything is written.
    private void EnsureStorageSpace(RepoRef repo, long extraBytes = 0)
    {
        var directory = repo.GitDir;
        while (!Directory.Exists(directory) && Path.GetDirectoryName(directory) is { } parent)
        {
            directory = parent;
        }

        var required = _options.MinFreeBytesForStorageWrites + extraBytes;
        var free = _freeSpace(directory);
        if (free < required)
        {
            throw new DiskSpaceException(directory, required, free);
        }
    }

    // Marks the repository as being written and gets it ready: leftover locks cleared, config and rule files current.
    private InFlightTable.Scope BeginWrite(RepoRef repo, IReadOnlyCollection<string>? ignorePatterns = null)
    {
        var scope = _inFlight.Enter(repo.GitDir);
        try
        {
            PrepareForWrite(repo, scope, ignorePatterns);
            return scope;
        }
        catch
        {
            scope.Dispose();
            throw;
        }
    }

    private void PrepareForWrite(RepoRef repo, InFlightTable.Scope scope, IReadOnlyCollection<string>? ignorePatterns)
    {
        // Writing config files into a folder that is not a repository would leave a half-made one behind.
        if (!IsInitialized(repo.GitDir))
        {
            throw new GitOperationException($"The repository at '{repo.GitDir}' does not exist.");
        }

        if (scope.IsAlone)
        {
            StaleLocks.RemoveAll(repo.GitDir, _classifying.ContainsKey);
        }

        EnsureRepoFiles(repo);
        if (ignorePatterns is not null)
        {
            RepoIgnoreFiles.WriteExclude(repo.GitDir, ignorePatterns);
        }
    }

    private GitInvocation Invocation(RepoRef repo, bool readOnly, params string[] arguments) =>
        new(arguments) { GitDir = repo.GitDir, WorkTree = repo.WorkTree, ReadOnly = readOnly };

    // Runs git and translates a failure into a typed exception.
    private async Task<GitResult> RunAsync(GitInvocation invocation, CancellationToken ct)
    {
        try
        {
            return await RunRawAsync(invocation, ct).ConfigureAwait(false);
        }
        catch (GitCommandException e)
        {
            throw GitFailure.ToException(e);
        }
    }

    // Runs git once; a non-zero exit that the invocation does not allow surfaces as the internal GitCommandException so the caller can read stderr.
    private Task<GitResult> RunOnceAsync(GitInvocation invocation, CancellationToken ct)
    {
        _options.InvocationObserver?.Invoke(invocation);
        RefuseRedirect(invocation);
        return _runner.RunAsync(invocation, ct);
    }

    // git takes the refs of a git directory that holds a pointer file named commondir from the folder the file names, and the environment
    // takes the objects from the git directory itself. SavePoint stores nothing that way, so no operation goes on from here.
    private static void RefuseRedirect(GitInvocation invocation)
    {
        if (invocation.GitDir is { } gitDir && Path.Combine(gitDir, "commondir") is var pointer && File.Exists(pointer))
        {
            throw new GitOperationException($"'{pointer}' points git at another folder, so '{gitDir}' was not used.");
        }
    }

    // For commands that write: another process holding a lock for a moment is retried with a short backoff.
    private async Task<GitResult> RunWriteAsync(Func<GitInvocation> invocation, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                return await RunRawAsync(invocation(), ct).ConfigureAwait(false);
            }
            catch (GitCommandException e) when (GitFailure.IsLockContention(e.StandardError) && attempt < _options.LockRetryDelays.Count)
            {
                await Task.Delay(_options.LockRetryDelays[attempt], ct).ConfigureAwait(false);
            }
            catch (GitCommandException e)
            {
                throw GitFailure.ToException(e);
            }
        }
    }
}
