using Savepoint.Core.Errors;
using Savepoint.Core.Paths;

namespace Savepoint.Core.Git;

public sealed partial class GitEngine
{
    // A file the point being left holds and the target does not, with the content the point held.
    internal readonly record struct GoneFile(string Path, string OldId);

    // Everything a switch is expected to change, known before git starts. Files the game's patterns name are not in it: the switch
    // leaves them alone, so they are listed apart. NamedInTarget is the part of Named that the target holds. Written is what git
    // writes as files, Removed what it removes (by the system's own idea of a name, which ignores case on Windows and macOS).
    private sealed record SwitchPlan(
        IReadOnlyList<TreeChange> Changes,
        IReadOnlySet<string> Touched,
        IReadOnlyList<GoneFile> Gone,
        IReadOnlySet<string> Named,
        IReadOnlySet<string> NamedInTarget,
        IReadOnlySet<string> Written,
        IReadOnlySet<string> Removed);

    // How the work tree's system compares two spellings of a path. Windows and macOS treat names that differ only in case as one file; so
    // does a drive mounted on Linux that ignores case (NTFS, exFAT), which only the drive can say.
    private StringComparer PathComparerFor(RepoRef repo) =>
        (_options.SystemIgnoresCase ?? (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS())) || DriveIgnoresCase(repo.WorkTree)
            ? StringComparer.OrdinalIgnoreCase
            : StringComparer.Ordinal;

    // True when the drive the folder is on treats names that differ only in case as one. Asked for the marker file the folder holds (the
    // folder may be the root of a drive, whose parent lives on another one), else for the folder (or the nearest folder above it with a
    // letter in its name), under the same name with every letter's case swapped: such a drive answers yes though the folder lists only the
    // original spelling. What the question cannot settle (a parent that cannot be listed, no letter on the way up) counts as a drive that
    // tells cases apart, which only makes the checks stricter.
    internal static bool DriveIgnoresCase(string folder) => DriveIgnoresCase(folder, path => Directory.Exists(path) || File.Exists(path), ListedName);

    // The file system's two answers are passed in, so a test can play a drive that tells cases apart on a machine whose drive does not.
    internal static bool DriveIgnoresCase(string folder, Func<string, bool> exists, Func<string, string, string> listedName)
    {
        if (exists(Path.Combine(folder, ConsentMarker.FileName)))
        {
            return AnswersToSwappedCase(folder, ConsentMarker.FileName, exists, listedName) == true;
        }

        for (var current = folder; Path.GetDirectoryName(current) is { } parent; current = parent)
        {
            if (AnswersToSwappedCase(parent, Path.GetFileName(current), exists, listedName) is { } answer)
            {
                return answer;
            }
        }

        return false;
    }

    // Whether the folder answers for the entry under its name with every letter's case swapped though it lists only the original; null
    // when the name has no letter to swap.
    private static bool? AnswersToSwappedCase(string folder, string name, Func<string, bool> exists, Func<string, string, string> listedName)
    {
        var swapped = new string([.. name.Select(letter => char.IsUpper(letter) ? char.ToLowerInvariant(letter) : char.ToUpperInvariant(letter))]);
        return string.Equals(swapped, name, StringComparison.Ordinal)
            ? null
            : exists(Path.Combine(folder, swapped)) && !string.Equals(listedName(folder, swapped), swapped, StringComparison.Ordinal);
    }

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
        var comparer = PathComparerFor(repo);
        var target = all.Where(change => change.Status != 'D').Select(change => change.Path).ToHashSet(comparer);
        var paths = all.Select(change => change.Path).ToHashSet(StringComparer.Ordinal);
        var named = await NamedAmongAsync(repo, paths, ct).ConfigureAwait(false);
        var gone = all
            .Where(change => change.Status == 'D' && !target.Contains(change.Path) && !named.Contains(change.Path))
            .DistinctBy(change => change.Path, StringComparer.Ordinal)
            .Select(change => new GoneFile(change.Path, change.OldId))
            .ToArray();
        return new SwitchPlan(
            [.. changes.Where(change => !named.Contains(change.Path))],
            paths.Where(path => !named.Contains(path)).ToHashSet(StringComparer.Ordinal),
            gone,
            named,
            all.Where(change => change.Status != 'D' && named.Contains(change.Path)).Select(change => change.Path).ToHashSet(StringComparer.Ordinal),
            all.Where(change => change.Status != 'D' && !named.Contains(change.Path)).Select(change => change.Path).ToHashSet(StringComparer.Ordinal),
            all.Where(change => change.Status == 'D' && !named.Contains(change.Path)).Select(change => change.Path).ToHashSet(comparer));
    }

    // Refuses, before anything is written, a switch that would run over work that is not saved: unsaved changes in the folder, or
    // a half-switched folder that is not being finished. Files the game's patterns name are never in the way: the switch leaves them.
    private async Task RequireNothingInTheWayAsync(RepoRef repo, string? from, string to, bool force, InterruptedSwitch? note, CancellationToken ct)
    {
        if (force)
        {
            return;
        }

        if (note is not null)
        {
            // Only the switch that was started may finish a half-switched folder; what else is in it cannot be told apart.
            if (!string.Equals(note.ToCommit, to, StringComparison.Ordinal))
            {
                throw new InterruptedSwitchException(repo.Role, note.ToCommit);
            }

            return;
        }

        var changed = ListChangedAsync(repo, hasCommits: from is not null, ct);
        var untracked = ListNewAsync(repo, ct);
        var changedPaths = await changed.ConfigureAwait(false);
        var newFiles = (await untracked.ConfigureAwait(false)).Files;
        if (changedPaths.Count > 0 || newFiles.Count > 0)
        {
            throw OnlyHeldChanges(repo, changedPaths, newFiles.Count)?.ToException(HeldFileOperation.Switch) ?? new DirtyWorkingTreeException(repo.Role);
        }
    }

    // git reads a file again when its timestamp is too new to trust (one stamped in the same second as the last index write), and a
    // file it cannot read (held open by another program on Windows, or not allowed to be read) comes back as "changed". When every
    // reported change is such a file the honest answer is that file, not "unsaved changes"; a change that can be read is unsaved work,
    // and that answer stays.
    private static HeldFile? OnlyHeldChanges(RepoRef repo, IReadOnlyList<string> changedPaths, int newFiles)
    {
        if (newFiles > 0)
        {
            return null;
        }

        HeldFile? held = null;
        foreach (var path in changedPaths)
        {
            var full = FullPath(repo, path);
            if (FileLockProbe.Probe(full) is not { } kind)
            {
                return null;
            }

            held ??= new HeldFile(full, kind);
        }

        return held;
    }
}
