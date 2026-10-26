using Savepoint.Core.Diagnostics;
using Savepoint.Core.Errors;

namespace Savepoint.Core.Git;

public sealed partial class GitEngine
{
    private const string _damagedIndexPrefix = "index.damaged-";

    // One repair at a time: two git calls of one operation can fail on the same damaged file together.
    private readonly SemaphoreSlim _repairGate = new(1, 1);

    // Only a view handed to work that holds the game's lock repairs; every other caller leaves a damaged file for the next locked operation.
    private readonly bool _repairsIndex;

    // How many set-aside indexes stay in a git directory; older ones are deleted by the next repair.
    private const int _asideIndexesKept = 3;

    // The same engine for work that holds the game's lock, which is the only work that may set the index aside.
    internal GitEngine ForGameLockHolder() => _repairsIndex ? this : new(this, repairsIndex: true);

    // Sets a damaged index right, when there is one, so a read made without the lock that failed on it can be run again. Needs an
    // engine from ForGameLockHolder. The check reads the whole index, so it is made only after such a read has failed.
    internal async Task RepairIndexIfDamagedAsync(RepoRef repo, CancellationToken ct)
    {
        repo = Normalize(repo);
        if (!IsInitialized(repo.GitDir))
        {
            return;
        }

        var check = new GitInvocation(["ls-files", "--cached", "--error-unmatch", "--", "savepoint-index-check"])
        {
            GitDir = repo.GitDir,
            WorkTree = repo.WorkTree,
            ReadOnly = true,
            AllowedExitCodes = [0, 1],
        };
        await RunAsync(check, ct).ConfigureAwait(false);
    }

    // Why the index could not be rebuilt. Only a latest snapshot that cannot be read is damage; a full disk, a lock another program
    // holds or a folder the account may not write are what they say, and a later try can succeed.
    internal Exception RepairFailureFor(GitCommandException e, string gitDir)
    {
        if (GitFailure.IsDiskFull(e.StandardError))
        {
            return new DiskSpaceException(gitDir, _options.MinFreeBytesForStorageWrites, _freeSpace(gitDir));
        }

        if (GitFailure.IsLockContention(e.StandardError))
        {
            return GitFailure.ToException(e);
        }

        if (GitFailure.IsAccessDenied(e.StandardError))
        {
            return new StorageWriteFailedException(gitDir, $"The index of '{gitDir}' could not be rebuilt because the folder cannot be written to: {e.Message}", e);
        }

        return new RepositoryDamagedException($"The index of '{gitDir}' is damaged and could not be rebuilt from the latest snapshot: {e.Message}", e);
    }

    private IDiagnosticLog Log => _runner.Options.Log ?? DiagnosticLog.None;

    // How the index file looked just before git ran, so a repair can tell that another caller has already replaced it.
    private readonly record struct IndexStamp(bool Exists, long Length, DateTime WrittenUtc)
    {
        public static IndexStamp Of(string gitDir)
        {
            var file = new FileInfo(Path.Combine(gitDir, "index"));
            return file.Exists ? new IndexStamp(true, file.Length, file.LastWriteTimeUtc) : default;
        }
    }

    // A call that uses a side index file of its own is not about the repository's index, and a stream cannot be fed twice.
    private static bool CanRepairIndexFor(GitInvocation invocation) =>
        invocation.GitDir is not null
        && invocation.StdinStream is null
        && !(invocation.ExtraEnvironment?.TryGetValue("GIT_INDEX_FILE", out var side) == true && side is not null);

    // An index file a power loss emptied or cut short makes every git command on the repository fail. The latest snapshot holds
    // everything the index is for, so the file is set aside and rebuilt from it, which never touches the game's files, and the
    // call that failed is run once more. The caller holds the game's lock, as for every other operation.
    private async Task<GitResult> RunRawAsync(GitInvocation invocation, CancellationToken ct)
    {
        var stamp = _repairsIndex && CanRepairIndexFor(invocation) ? IndexStamp.Of(invocation.GitDir!) : (IndexStamp?)null;
        try
        {
            var result = await RunOnceAsync(invocation, ct).ConfigureAwait(false);
            if (stamp is null || result.ExitCode == 0 || !GitFailure.IsIndexDamage(result.StandardError))
            {
                return result;
            }
        }
        catch (GitCommandException e) when (stamp is not null && GitFailure.IsIndexDamage(e.StandardError))
        {
        }

        await RepairIndexAsync(invocation, stamp.Value, ct).ConfigureAwait(false);

        try
        {
            var again = await RunOnceAsync(invocation, ct).ConfigureAwait(false);
            if (again.ExitCode == 0 || !GitFailure.IsIndexDamage(again.StandardError))
            {
                return again;
            }
        }
        catch (GitCommandException e) when (GitFailure.IsIndexDamage(e.StandardError))
        {
        }

        var gitDir = invocation.GitDir!;
        Log.Error($"The index of '{gitDir}' was damaged again right after it was rebuilt.");
        throw new RepositoryDamagedException($"The index of '{gitDir}' was damaged again right after it was rebuilt.");
    }

    private async Task RepairIndexAsync(GitInvocation invocation, IndexStamp failedOn, CancellationToken ct)
    {
        var gitDir = invocation.GitDir!;
        await _repairGate.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            // Another caller got here first, or a write replaced the file since git read it: run again on what is there now.
            if (IndexStamp.Of(gitDir) != failedOn)
            {
                return;
            }

            var index = Path.Combine(gitDir, "index");
            string? aside = null;
            if (failedOn.Exists)
            {
                aside = AsideName(gitDir);
                File.Move(index, aside);
            }

            var rebuilt = false;
            try
            {
                if (IsUnborn(gitDir))
                {
                    Log.Information($"The index of '{gitDir}' was damaged. It was set aside as '{Path.GetFileName(aside)}'; nothing is saved there yet, so a new one starts empty.");
                }
                else
                {
                    await RunOnceAsync(new GitInvocation(["read-tree", "HEAD"]) { GitDir = gitDir, WorkTree = invocation.WorkTree }, ct).ConfigureAwait(false);
                    Log.Information($"The index of '{gitDir}' was damaged. It was set aside as '{Path.GetFileName(aside)}' and rebuilt from the latest snapshot.");
                }

                rebuilt = true;
                PruneAsideIndexes(gitDir);
            }
            catch (GitCommandException e)
            {
                var failure = RepairFailureFor(e, gitDir);
                if (failure is RepositoryDamagedException)
                {
                    Log.Error(failure.Message);
                }
                else
                {
                    Log.Warning(failure.Message);
                }

                throw failure;
            }
            finally
            {
                if (!rebuilt && aside is not null)
                {
                    PutBack(aside, index);
                }
            }
        }
        finally
        {
            _repairGate.Release();
        }
    }

    // A branch with no ref yet is a folder nothing was saved from. A ref file that exists but is empty or unreadable is not that:
    // it is damage to the history itself, and building a new history over it would hide the old one.
    private static bool IsUnborn(string gitDir)
    {
        HeadState head;
        try
        {
            head = GitDirFiles.ReadHead(gitDir);
        }
        catch (GitOperationException)
        {
            return false;
        }

        if (head.Branch is null)
        {
            return false;
        }

        var reference = GitDirFiles.BranchPrefix + head.Branch;
        if (File.Exists(Path.Combine(gitDir, reference.Replace('/', Path.DirectorySeparatorChar))))
        {
            return false;
        }

        var packed = AtomicText.ReadOrNull(Path.Combine(gitDir, "packed-refs"));
        return packed is null || !packed.Split('\n').Any(line => line.TrimEnd('\r').EndsWith(" " + reference, StringComparison.Ordinal));
    }

    private string AsideName(string gitDir)
    {
        var stem = Path.Combine(gitDir, _damagedIndexPrefix + _options.Clock.GetUtcNow().ToString("yyyyMMdd'T'HHmmssfff", System.Globalization.CultureInfo.InvariantCulture));
        var name = stem;
        for (var n = 2; File.Exists(name); n++)
        {
            name = stem + "-" + n.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        return name;
    }

    // Set-aside indexes are kept for a support request, not for ever: the newest few stay (the names sort by time).
    private void PruneAsideIndexes(string gitDir)
    {
        foreach (var old in Directory.GetFiles(gitDir, _damagedIndexPrefix + "*").Order(StringComparer.Ordinal).SkipLast(_asideIndexesKept))
        {
            try
            {
                File.Delete(old);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                Log.Warning($"The old damaged index '{old}' could not be deleted.", e);
            }
        }
    }

    // A repair that could not finish leaves the repository as it was found.
    private void PutBack(string aside, string index)
    {
        try
        {
            File.Delete(index);
            File.Move(aside, index);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Log.Warning($"The damaged index '{aside}' could not be put back as '{index}'.", e);
        }
    }
}
