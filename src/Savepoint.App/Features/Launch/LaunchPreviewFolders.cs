using Savepoint.Core.Api;
using Savepoint.Core.Catalog;

namespace Savepoint.App.Features.Launch;

/// <summary>The folders of a launch preview as one list, in the order a switch writes them: the game folder, the mods folder, the saves folder.</summary>
internal static class LaunchPreviewFolders
{
    /// <summary>The folders the game has, install first.</summary>
    /// <param name="preview">The preview.</param>
    public static IReadOnlyList<FolderLaunchState> Of(LaunchPreview preview) =>
        new[] { preview.Install, preview.Mods, preview.External }.OfType<FolderLaunchState>().ToList();

    /// <summary>One folder's state; the folder must be one the game has.</summary>
    /// <param name="preview">The preview.</param>
    /// <param name="role">Which folder.</param>
    public static FolderLaunchState State(LaunchPreview preview, RepoRole role) => role switch
    {
        RepoRole.Install => preview.Install,
        RepoRole.Mods => preview.Mods!,
        _ => preview.External!,
    };
}
