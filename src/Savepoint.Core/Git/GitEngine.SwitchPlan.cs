using Savepoint.Core.Errors;

namespace Savepoint.Core.Git;

public sealed partial class GitEngine
{
    // A file the point being left holds and the target does not, with the content the point held.
    internal readonly record struct GoneFile(string Path, string OldId);

    // Everything a switch is expected to change, known before git starts.
    private sealed record SwitchPlan(IReadOnlyList<TreeChange> Changes, IReadOnlySet<string> Touched, IReadOnlyList<GoneFile> Gone);

    private static StringComparer PathComparer => OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    // What the switch will write and remove. After an unfinished switch HEAD may already be at the target, so the point the
    // unfinished switch started from counts too.
    private async Task<SwitchPlan> PlanSwitchAsync(RepoRef repo, string? from, string to, InterruptedSwitch? note, CancellationToken ct)
    {
        var changes = await DiffTreesAsync(repo, from ?? GitDirFiles.EmptyTree, to, ct).ConfigureAwait(false);
        List<TreeChange> all = [.. changes];
        if (note?.FromCommit is { } earlier && !string.Equals(earlier, from, StringComparison.Ordinal))
        {
            all.AddRange(await DiffTreesAsync(repo, earlier, to, ct).ConfigureAwait(false));
        }

        // A name that differs from a removed one only in case is the same file on this system, and the target holds it.
        var target = all.Where(change => change.Status != 'D').Select(change => change.Path).ToHashSet(PathComparer);
        var gone = all
            .Where(change => change.Status == 'D' && !target.Contains(change.Path))
            .DistinctBy(change => change.Path, StringComparer.Ordinal)
            .Select(change => new GoneFile(change.Path, change.OldId))
            .ToArray();
        return new SwitchPlan(changes, all.Select(change => change.Path).ToHashSet(StringComparer.Ordinal), gone);
    }

    // Refuses, before anything is written, a switch that would run over work that is not saved: unsaved changes in the
    // folder, a half-switched folder that is not being finished, or a file the game's patterns name that the switch would
    // overwrite or delete.
    private async Task RequireNothingInTheWayAsync(RepoRef repo, string? from, string to, bool force, InterruptedSwitch? note, SwitchPlan plan, CancellationToken ct)
    {
        var excluded = ListExcludedAsync(repo, ct);
        if (!force)
        {
            if (note is not null)
            {
                // Only the switch that was started may finish a half-switched folder; what else is in it cannot be told apart.
                if (!string.Equals(note.ToCommit, to, StringComparison.Ordinal))
                {
                    throw new InterruptedSwitchException(repo.Role, note.ToCommit);
                }
            }
            else
            {
                var changed = ListChangedAsync(repo, hasCommits: from is not null, ct);
                var untracked = ListNewAsync(repo, ct);
                if ((await changed.ConfigureAwait(false)).Count > 0 || (await untracked.ConfigureAwait(false)).Files.Count > 0)
                {
                    throw new DirtyWorkingTreeException(repo.Role);
                }
            }
        }

        var (files, folders) = await excluded.ConfigureAwait(false);
        foreach (var path in plan.Touched)
        {
            if ((files.Contains(path) || folders.Any(folder => path.StartsWith(folder, StringComparison.Ordinal))) && File.Exists(FullPath(repo, path)))
            {
                throw new DirtyWorkingTreeException(repo.Role);
            }
        }
    }

    // Files the game's patterns name: recorded ones (a switch would overwrite or delete them) and unrecorded ones (a switch
    // would overwrite them). A folder that is wholly excluded comes back as one entry with a trailing slash.
    private async Task<(HashSet<string> Files, List<string> Folders)> ListExcludedAsync(RepoRef repo, CancellationToken ct)
    {
        var result = await RunAsync(Invocation(repo, readOnly: true, "ls-files", "-z", "-c", "-o", "-i", "--directory", ExcludeFromArgument(repo)), ct).ConfigureAwait(false);
        var files = new HashSet<string>(StringComparer.Ordinal);
        var folders = new List<string>();
        foreach (var entry in result.SplitStandardOutputOnNul())
        {
            if (entry.EndsWith('/'))
            {
                folders.Add(entry);
            }
            else
            {
                files.Add(entry);
            }
        }

        return (files, folders);
    }
}
