namespace Savepoint.Core.Tests.Backend;

// The installed app runs from a folder next to Update.exe, and an update or an uninstall deletes the whole install folder, so storage
// moved into it would go with it (D38).
public class RelocationInstallFolderTests
{
    // The workspace's app folder plays "current"; its parent plays the install folder once Update.exe is there.
    private static string MarkAsInstallFolder(BackendWorkspace workspace)
    {
        var installFolder = Path.GetDirectoryName(workspace.InstallDirectory)!;
        BackendWorkspace.WriteFile(Path.Combine(installFolder, "Update.exe"), "not a real updater");
        return installFolder;
    }

    [Fact(Timeout = 120_000)]
    public async Task A_destination_inside_the_install_folder_is_blocked_in_the_preview_and_refused_by_the_move()
    {
        using var workspace = new BackendWorkspace();
        await workspace.AddGameAsync();
        MarkAsInstallFolder(workspace);
        var destination = Path.Combine(workspace.Root, "new-storage");

        var preview = await workspace.Settings.GetRelocationPreviewAsync(destination, workspace.Token);
        var started = await workspace.Settings.RelocateStorageAsync(destination);

        Assert.Equal("error.storageRelocation.insideInstallFolder", preview.Value!.BlockedBy?.MessageId);
        Assert.Equal(destination, preview.Value.BlockedBy!.Args["path"]);
        Assert.Equal("error.storageRelocation.insideInstallFolder", started.Error?.MessageId);
        Assert.False(Directory.Exists(destination));
        Assert.Equal(workspace.DefaultStorageRoot, (await workspace.Settings.GetAsync(workspace.Token)).Value!.StorageRoot);
    }

    [Fact(Timeout = 120_000)]
    public async Task A_destination_beside_the_install_folder_is_not_blocked()
    {
        using var workspace = new BackendWorkspace();
        await workspace.AddGameAsync();
        var installFolder = MarkAsInstallFolder(workspace);
        var beside = installFolder + "-data";

        var preview = await workspace.Settings.GetRelocationPreviewAsync(beside, workspace.Token);

        Assert.Null(preview.Value!.BlockedBy);
    }
}
