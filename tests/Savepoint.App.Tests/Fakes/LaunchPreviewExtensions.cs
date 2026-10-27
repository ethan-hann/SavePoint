using Savepoint.Core.Api;
using Savepoint.Core.Catalog;

namespace Savepoint.App.Tests.Fakes;

// Edits one folder of a scripted preview, keeping the folders in the order a switch writes them.
internal static class LaunchPreviewExtensions
{
    private static readonly RepoRole[] _switchOrder = [RepoRole.Install, RepoRole.Mods, RepoRole.External];

    // Replaces the folder of the same role, or adds it when the game had none.
    public static LaunchPreview WithFolder(this LaunchPreview preview, FolderLaunchState folder) => preview with
    {
        Folders = [.. preview.Folders.Where(existing => existing.Role != folder.Role).Append(folder).OrderBy(state => Array.IndexOf(_switchOrder, state.Role))],
    };

    // Changes the folder of a role; the game must have it.
    public static LaunchPreview WithFolder(this LaunchPreview preview, RepoRole role, Func<FolderLaunchState, FolderLaunchState> change) =>
        preview.WithFolder(change(preview.Folder(role) ?? throw new InvalidOperationException($"The preview has no {role} folder.")));

    public static LaunchPreview WithoutFolder(this LaunchPreview preview, RepoRole role) => preview with
    {
        Folders = [.. preview.Folders.Where(folder => folder.Role != role)],
    };
}
