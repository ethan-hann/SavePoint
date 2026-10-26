using System.Collections.Concurrent;
using System.Globalization;
using Savepoint.Core.Diagnostics;
using Savepoint.Core.Errors;

namespace Savepoint.Core.Git;

public sealed partial class GitEngine
{
    // The name ends in .tmp, which deleting a repository accepts, and starts with nothing the sweep of stale files knows.
    private const string _asideIndexPrefix = "savepoint-damaged-index-";

    // How many set-aside indexes stay in a git directory; older ones are deleted by the next repair.
    private const int _asideIndexesKept = 3;

    // One repair at a time: two git calls of one operation can fail on the same damaged file together.
    private readonly SemaphoreSlim _repairGate = new(1, 1);

    // Only a view handed to work that holds the game's lock repairs; every other caller leaves a damaged file for the next locked operation.
    private readonly bool _repairsIndex;

    // The git directories whose current operation has written the index itself.
    private readonly ConcurrentDictionary<string, byte> _indexWritten = new(OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);

    // The same engine for work that holds the game's lock, which is the only work that may set the index aside.
    internal GitEngine ForGameLockHolder() => _repairsIndex ? this : new(this, repairsIndex: true);

    // From here to the end of the scope the current operation has written the index itself, so a rebuild from the last snapshot would
    // undo what it wrote. Damage then ends the operation with an error and the next operation repairs.
    internal IDisposable NoRepairWhileWriting(string gitDir)
    {
        _indexWritten[gitDir] = 0;
        return new Release(_indexWritten, gitDir);
    }

    private sealed class Release(ConcurrentDictionary<string, byte> held, string gitDir) : IDisposable
    {
        public void Dispose() => held.TryRemove(gitDir, out _);
    }

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
            return new StorageWriteFailedException(Path.GetDirectoryName(gitDir)!, $"The index of '{gitDir}' could not be rebuilt because the folder cannot be written to: {e.Message}", e);
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
    // everything the index is for, so a new one is built from it beside the old, which never touches the game's files, and swapped in;
    // the call that failed is run once more. The caller holds the game's lock, as for every other operation.
    private async Task<GitResult> RunRawAsync(GitInvocation invocation, CancellationToken ct)
    {
        var repairs = _repairsIndex && CanRepairIndexFor(invocation) && !_indexWritten.ContainsKey(invocation.GitDir!);
        var stamp = repairs ? IndexStamp.Of(invocation.GitDir!) : (IndexStamp?)null;
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
        var rebuilt = Path.Combine(gitDir, $"{AlignIndexPrefix}repair-{Guid.NewGuid():N}.index.tmp");
        try
        {
            // Another caller got here first, or a write replaced the file since git read it: run again on what is there now.
            if (IndexStamp.Of(gitDir) != failedOn)
            {
                return;
            }

            try
            {
                await BuildIndexAsync(invocation, rebuilt, ct).ConfigureAwait(false);
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

            var aside = await SwapInIndexAsync(gitDir, rebuilt, ct).ConfigureAwait(false);
            var kept = aside is null ? "no file was left to keep" : $"set aside as '{Path.GetFileName(aside)}'";
            Log.Information($"The index of '{gitDir}' was damaged ({kept}) and was rebuilt from the latest snapshot.");
            PruneAsideIndexes(gitDir);
        }
        finally
        {
            TryDeleteFile(rebuilt);
            _repairGate.Release();
        }
    }

    // The new index is built in a file of its own, so the real one is never missing. Its entries come from the latest snapshot with no stamp
    // for any file, and a refresh gives each its stamp now: left cold, every later status would read every file in full. A refresh can
    // fail for a file another program holds; that costs only a later read of it. A folder nothing was saved from starts with an empty index.
    private async Task BuildIndexAsync(GitInvocation invocation, string target, CancellationToken ct)
    {
        var gitDir = invocation.GitDir!;
        var environment = new Dictionary<string, string?> { ["GIT_INDEX_FILE"] = target };
        var unborn = IsUnborn(gitDir);
        string[] read = unborn ? ["read-tree", "--empty"] : ["read-tree", "HEAD"];

        // A question asked of an empty folder (see Classify) must not leave the rebuilt index stamped against that folder.
        var folder = invocation.TrackedFolder ?? invocation.WorkTree;
        await RunOnceAsync(new GitInvocation(read) { GitDir = gitDir, WorkTree = folder, ExtraEnvironment = environment }, ct).ConfigureAwait(false);
        if (!unborn)
        {
            await RunOnceAsync(new GitInvocation(["update-index", "-q", "--refresh"]) { GitDir = gitDir, WorkTree = folder, ExtraEnvironment = environment, AllowedExitCodes = _anyExitCode }, ct).ConfigureAwait(false);
        }
    }

    // The new index goes in the way git puts one in: its lock is taken first, so a git process that holds the lock is waited for and the
    // index is never replaced under it. The damaged file is renamed in the same step. Returns the name it was set aside under, or null
    // when there was no file.
    private async Task<string?> SwapInIndexAsync(string gitDir, string rebuilt, CancellationToken ct)
    {
        var index = Path.Combine(gitDir, "index");
        var gate = index + ".lock";
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                File.Move(rebuilt, gate, overwrite: false);
                break;
            }
            catch (IOException) when (File.Exists(gate))
            {
                if (attempt >= _options.LockRetryDelays.Count)
                {
                    throw new GitOperationException(gate, $"git could not take its lock: '{gate}' exists.");
                }

                await Task.Delay(_options.LockRetryDelays[attempt], ct).ConfigureAwait(false);
            }
        }

        try
        {
            var aside = File.Exists(index) ? AsideName(gitDir) : null;
            for (var attempt = 0; ; attempt++)
            {
                try
                {
                    if (aside is null)
                    {
                        File.Move(gate, index, overwrite: true);
                    }
                    else
                    {
                        File.Replace(gate, index, aside);
                    }

                    return aside;
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    // A scanner or a backup tool can hold the file for a moment.
                    if (attempt >= _options.LockRetryDelays.Count)
                    {
                        throw SwapFailure(gitDir, index, e);
                    }

                    await Task.Delay(_options.LockRetryDelays[attempt], ct).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            TryDeleteFile(gate);
        }
    }

    // A file another program holds open is the failure that closing programs fixes; anything else is a folder SavePoint cannot write.
    private static SavepointException SwapFailure(string gitDir, string index, Exception e) =>
        e is IOException held && FileLockProbe.IsSharingViolation(held)
            ? new GitOperationException(index, $"'{index}' is held by another program and could not be replaced: {e.Message}")
            : new StorageWriteFailedException(Path.GetDirectoryName(gitDir)!, $"'{index}' could not be replaced: {e.Message}", e);

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
        var stem = Path.Combine(gitDir, _asideIndexPrefix + _options.Clock.GetUtcNow().ToString("yyyyMMdd'T'HHmmssfff", CultureInfo.InvariantCulture));
        var name = stem + ".tmp";
        for (var n = 2; File.Exists(name); n++)
        {
            name = stem + "-" + n.ToString(CultureInfo.InvariantCulture) + ".tmp";
        }

        return name;
    }

    // Set-aside indexes are kept for a support request, not for ever: the newest few stay (the names sort by time).
    private void PruneAsideIndexes(string gitDir)
    {
        foreach (var old in Directory.GetFiles(gitDir, _asideIndexPrefix + "*").Order(StringComparer.Ordinal).SkipLast(_asideIndexesKept))
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

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // A leftover carries a name the next sweep of stale files removes.
        }
    }
}
