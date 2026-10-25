using Savepoint.Core.Tests.Platform;

namespace Savepoint.Core.Tests.Backend;

// The Windows installer owns %LOCALAPPDATA%\<pack id> and clears it on install and uninstall, so storage moved anywhere inside it
// would go with it, whether or not the app runs from there.
public class RelocationInstallerFolderTests
{
    private static BackendWorkspace Workspace() => new(options =>
    {
        var root = Path.GetDirectoryName(options.InstallDirectory)!;
        return options with
        {
            Platform = new RelocatedPlatform(options.Platform!, options.Platform!.Directories with { LegacyLocalDir = Path.Combine(root, "Local", "SavePoint") }),
        };
    });

    [Theory(Timeout = 120_000)]
    [InlineData("SavePoint")]
    [InlineData("SavePoint/Storage")]
    [InlineData("SavePoint/current/data")]
    public async Task A_destination_in_the_installers_folder_is_refused_by_the_preview_and_the_move(string relative)
    {
        using var workspace = Workspace();
        await workspace.AddGameAsync();
        var destination = Path.Combine(workspace.Root, "Local", relative);

        var preview = await workspace.Settings.GetRelocationPreviewAsync(destination, workspace.Token);
        var started = await workspace.Settings.RelocateStorageAsync(destination);

        Assert.Equal("error.storageRelocation.insideInstallFolder", preview.Value!.BlockedBy?.MessageId);
        Assert.Equal("error.storageRelocation.insideInstallFolder", started.Error?.MessageId);
        Assert.False(Directory.Exists(destination));
    }

    [Fact(Timeout = 120_000)]
    public async Task A_sibling_of_the_installers_folder_is_not_refused()
    {
        using var workspace = Workspace();
        await workspace.AddGameAsync();

        foreach (var sibling in new[] { "SavePoint Data", "SavePoint-data", "SavePointOther" })
        {
            var preview = await workspace.Settings.GetRelocationPreviewAsync(Path.Combine(workspace.Root, "Local", sibling, "Storage"), workspace.Token);

            Assert.Null(preview.Value!.BlockedBy);
        }
    }
}
