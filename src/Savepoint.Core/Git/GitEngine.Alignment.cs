using System.Text;
using Savepoint.Core.Errors;

namespace Savepoint.Core.Git;

// For the files the game's patterns name the switch makes the index say what the target says, so git sees nothing to do. That is done to a
// copy of the index and compared with the original before it replaces it, so a failure leaves the real index as it was. An entry the
// alignment drops that the patterns do not name is a displacement: an index cannot hold a file and a folder of one name, so naming
// logs/run.txt in the target takes away the entry for the file logs. That file is clean profile content the target lacks, and the
// switch removes it itself, under the note, before git starts: git would otherwise see an untracked file in its way and refuse.
public sealed partial class GitEngine
{
    // The copies of the index a switch works on; a stop can leave one behind, and the stale-lock sweep removes every file with this prefix.
    // They end in .tmp, which is what a git directory that is being deleted counts as its own.
    internal const string AlignIndexPrefix = "savepoint-align-";

    // What a refusal needs to undo an alignment: the index as it was (null when there was none) and the files removed for the switch.
    private sealed class AlignedIndex(string? saved, IReadOnlyList<string> removed) : IDisposable
    {
        public string? Saved { get; } = saved;

        public IReadOnlyList<string> Removed { get; } = removed;

        public void Dispose()
        {
            if (Saved is not null)
            {
                TryDelete(Saved);
            }
        }
    }

    // Null when the patterns name nothing the switch touches. A failure before the new index is in place is a refusal: the index and the
    // files are as they were and a note this call wrote goes. If the files cannot be put back the note stays.
    private async Task<AlignedIndex?> AlignIndexAsync(RepoRef repo, string tree, SwitchPlan plan, IReadOnlyCollection<string> marks, bool force, bool ownsNote, CancellationToken ct)
    {
        if (plan.Named.Count == 0)
        {
            return null;
        }

        var index = Path.Combine(repo.GitDir, "index");
        var id = Guid.NewGuid().ToString("N");
        var saved = Path.Combine(repo.GitDir, $"{AlignIndexPrefix}{id}.saved.tmp");
        var work = Path.Combine(repo.GitDir, $"{AlignIndexPrefix}{id}.index.tmp");
        var hadIndex = File.Exists(index);
        List<string> removed = [];
        var done = false;
        try
        {
            if (hadIndex)
            {
                File.Copy(index, saved);
                File.Copy(index, work);
            }

            var environment = new Dictionary<string, string?> { ["GIT_INDEX_FILE"] = work };
            await WriteTargetEntriesAsync(repo, tree, plan, environment, ct).ConfigureAwait(false);

            // A forced checkout would write these files, and every checkout starts by refreshing the index, which would read each one that
            // differs; git does neither for an entry marked skip-worktree.
            await SetSkipWorktreeAsync(repo, on: true, marks, ct, environment).ConfigureAwait(false);

            if (hadIndex)
            {
                var displaced = await DisplacedEntriesAsync(repo, saved, work, plan, ct).ConfigureAwait(false);
                RemoveDisplaced(repo, plan, displaced, force, removed);
            }

            await ReplaceIndexAsync(repo, work, ct).ConfigureAwait(false);
            done = true;
            return new AlignedIndex(hadIndex ? saved : null, removed);
        }
        catch (SavepointException)
        {
            if (await TryPutBackFilesAsync(repo, removed).ConfigureAwait(false))
            {
                await ClearNoteIfAsync(ownsNote, repo).ConfigureAwait(false);
            }

            throw;
        }
        finally
        {
            TryDelete(work);
            if (!done)
            {
                TryDelete(saved);
            }
        }
    }

    // Makes the entry of every named path say what the target says. A file whose size is not the size of the target's blob cannot hold the
    // target's bytes, so its entry is written from the plan and git never reads the file (a reset would read each one). A file of the right
    // size might hold them, and a reset reads it once so that, if it does, the entry keeps the file's stamp.
    private async Task WriteTargetEntriesAsync(RepoRef repo, string tree, SwitchPlan plan, Dictionary<string, string?> environment, CancellationToken ct)
    {
        var present = plan.NamedChanges.Where(change => change.Status != 'D').ToArray();
        var sizes = await ObjectSizesAsync(repo, present.Select(change => change.NewId), ct).ConfigureAwait(false);
        var removals = plan.NamedChanges.Where(change => change.Status == 'D').Select(change => change.Path).ToArray();
        var written = present.Where(change => !MayHoldTargetBytes(repo, change, sizes)).ToArray();
        var read = present.Except(written).Select(change => change.Path).ToArray();

        // The reset refreshes the whole index, and an entry written from the plan has no stamp, so it would be read there: those go in after.
        // Removals go in before the reset so that the index it refreshes is as small as it can be.
        if (read.Length == 0)
        {
            await WriteIndexInfoAsync(repo, removals, written, environment, ct).ConfigureAwait(false);
            return;
        }

        await WriteIndexInfoAsync(repo, removals, [], environment, ct).ConfigureAwait(false);
        await RunWriteAsync(() => (Invocation(repo, readOnly: false, "reset", "--quiet", tree) with { ExtraEnvironment = environment }).WithPathspecs(read), ct).ConfigureAwait(false);
        await WriteIndexInfoAsync(repo, [], written, environment, ct).ConfigureAwait(false);
    }

    // False only when the work tree holds an ordinary file of another size than the target's blob (or holds none): then nothing needs reading.
    private bool MayHoldTargetBytes(RepoRef repo, TreeChange change, Dictionary<string, long> sizes)
    {
        if (change.NewMode is not ("100644" or "100755") || !sizes.TryGetValue(change.NewId, out var size))
        {
            return true;
        }

        try
        {
            var file = new FileInfo(FullPath(repo, change.Path));
            return file.Exists && ((file.Attributes & FileAttributes.ReparsePoint) != 0 || file.Length == size);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    // update-index --index-info takes "mode id TAB path" lines; mode 0 removes the path. Nothing to write runs nothing.
    private async Task WriteIndexInfoAsync(RepoRef repo, IReadOnlyList<string> removals, IReadOnlyList<TreeChange> additions, Dictionary<string, string?> environment, CancellationToken ct)
    {
        if (removals.Count == 0 && additions.Count == 0)
        {
            return;
        }

        var lines = new StringBuilder();
        foreach (var path in removals)
        {
            lines.Append("0 ").Append(_zeroId).Append('\t').Append(path).Append('\0');
        }

        foreach (var change in additions)
        {
            lines.Append(change.NewMode).Append(' ').Append(change.NewId).Append('\t').Append(change.Path).Append('\0');
        }

        var stdin = lines.ToString();
        var result = await RunWriteAsync(() => Invocation(repo, readOnly: false, "update-index", "-z", "--index-info") with { Stdin = stdin, ExtraEnvironment = environment }, ct).ConfigureAwait(false);

        // On Windows git skips a name the system cannot hold, says so and still exits 0; the reset this replaced failed on such a name.
        if (result.StandardError.Split('\n').FirstOrDefault(line => line.StartsWith("Ignoring path ", StringComparison.Ordinal)) is { } skipped)
        {
            throw new GitOperationException(skipped.Trim()) { StandardError = result.StandardError };
        }
    }

    // The entries the alignment took out of the index that the patterns do not name.
    private async Task<IReadOnlyList<string>> DisplacedEntriesAsync(RepoRef repo, string before, string after, SwitchPlan plan, CancellationToken ct)
    {
        var listBefore = RunAsync(Invocation(repo, readOnly: true, "ls-files", "-z") with { ExtraEnvironment = new Dictionary<string, string?> { ["GIT_INDEX_FILE"] = before } }, ct);
        var listAfter = RunAsync(Invocation(repo, readOnly: true, "ls-files", "-z") with { ExtraEnvironment = new Dictionary<string, string?> { ["GIT_INDEX_FILE"] = after } }, ct);
        var kept = new HashSet<string>((await listAfter.ConfigureAwait(false)).SplitStandardOutputOnNul(), StringComparer.Ordinal);
        var aligned = new HashSet<string>(plan.Named, PathComparerFor(repo));
        return [.. (await listBefore.ConfigureAwait(false)).SplitStandardOutputOnNul().Where(entry => !kept.Contains(entry) && !aligned.Contains(entry))];
    }

    // Removes the file of each displaced entry when it is exactly what the point held (a forced switch discards edits to a recorded file
    // on purpose). An entry the switch has no reason to drop, or a file that is not the point's, is the player's: the switch is refused.
    private void RemoveDisplaced(RepoRef repo, SwitchPlan plan, IReadOnlyList<string> displaced, bool force, List<string> removed)
    {
        var comparer = PathComparerFor(repo);
        if (displaced.Count > 0)
        {
            RecheckBeforeFileChanges(repo, "remove-displaced");
        }

        // Every file is looked at and removed in the folder that was approved, not wherever a link leads by now.
        var approved = repo.Guard is { } guard ? repo with { WorkTree = guard.RealPath } : repo;
        foreach (var path in displaced)
        {
            if (plan.Gone.Where(entry => comparer.Equals(entry.Path, path)).Select(entry => (GoneFile?)entry).FirstOrDefault() is not { } gone)
            {
                throw new DirtyWorkingTreeException(repo.Role);
            }

            var full = FullPath(approved, path);
            if (!File.Exists(full))
            {
                continue;
            }

            var ours = force ? !LinkedFolders.IsInsideLink(approved.WorkTree, path) && HasExactName(full) : IsLeftover(approved, gone);
            if (!ours)
            {
                throw new DirtyWorkingTreeException(repo.Role);
            }

            TryDelete(full);
            if (File.Exists(full))
            {
                throw FileLockProbe.Probe(full) is { } kind
                    ? new HeldFile(FullPath(repo, path), kind).ToException(HeldFileOperation.Switch)
                    : new GitOperationException(FullPath(repo, path), NotUpdatedMessage(FileStep.RemoveDisplaced, [path]));
            }

            removed.Add(path);
        }
    }

    // Puts the file of each entry back from the index, which is the original while this runs. checkout-index takes no lock.
    private async Task<bool> TryPutBackFilesAsync(RepoRef repo, IReadOnlyList<string> paths)
    {
        if (paths.Count == 0)
        {
            return true;
        }

        try
        {
            await RunAsync(Invocation(repo, readOnly: false, "checkout-index", "-f", "-z", "--stdin") with { Stdin = NulList(paths) }, CancellationToken.None).ConfigureAwait(false);
            return true;
        }
        catch (GitOperationException)
        {
            return false;
        }
    }

    // Puts the index back as it was before the alignment and brings back the files removed for the switch; false when it cannot be done.
    private async Task<bool> TryRestoreAsync(RepoRef repo, AlignedIndex? aligned)
    {
        if (aligned is null)
        {
            return true;
        }

        try
        {
            if (aligned.Saved is { } saved && File.Exists(saved))
            {
                await ReplaceIndexAsync(repo, saved, CancellationToken.None).ConfigureAwait(false);
            }
            else
            {
                File.Delete(Path.Combine(repo.GitDir, "index"));
            }
        }
        catch (Exception e) when (e is GitOperationException or IOException or UnauthorizedAccessException)
        {
            return false;
        }

        return await TryPutBackFilesAsync(repo, aligned.Removed).ConfigureAwait(false);
    }

    // The new index goes in the way git puts one in: its lock is taken first, so a git process that holds the lock is waited for and the
    // index is never replaced under it. The source is consumed.
    private async Task ReplaceIndexAsync(RepoRef repo, string source, CancellationToken ct)
    {
        var index = Path.Combine(repo.GitDir, "index");
        var gate = index + ".lock";
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                File.Move(source, gate, overwrite: false);
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
            File.Move(gate, index, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            TryDelete(gate);
            throw new GitOperationException($"The index '{index}' could not be replaced: {e.Message}");
        }
    }
}
