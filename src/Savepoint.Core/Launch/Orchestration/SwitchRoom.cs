using Savepoint.Core.Git;

namespace Savepoint.Core.Launch.Orchestration;

/// <summary>What one folder's switch adds, and the volume it is written to.</summary>
/// <param name="Repo">The folder.</param>
/// <param name="Volume">The volume that holds the folder, or null when it cannot be told.</param>
/// <param name="GrowthBytes">The bytes the switch adds to the folder, never negative.</param>
internal readonly record struct FolderGrowth(RepoRef Repo, string? Volume, long GrowthBytes);

/// <summary>Adds up what a launch's switches need from each volume that holds more than one of its folders.</summary>
internal static class SwitchRoom
{
    /// <summary>
    /// The volumes that hold two or more of the folders, each with the first of those folders (in switch order) and the sum of their growth.
    /// Only growth is summed: a folder that shrinks frees its space as its files are removed, which is after another folder may need it,
    /// so a shrink never makes room for another folder's growth. A folder whose volume is unknown is judged on its own, not here.
    /// </summary>
    /// <param name="folders">The folders in the order they are switched.</param>
    public static IReadOnlyList<(RepoRef Repo, long GrowthBytes)> Shared(IReadOnlyList<FolderGrowth> folders) =>
    [
        .. folders
            .Where(folder => folder.Volume is not null)
            .GroupBy(folder => folder.Volume!, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => (group.First().Repo, group.Sum(folder => Math.Max(0, folder.GrowthBytes)))),
    ];
}
