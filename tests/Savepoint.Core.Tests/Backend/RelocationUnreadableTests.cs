using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace Savepoint.Core.Tests.Backend;

// A move that cannot read part of the storage fails by name instead of leaving that part behind (D31).
public class RelocationUnreadableTests
{
    [Fact]
    public async Task A_folder_inside_the_storage_that_cannot_be_read_fails_the_move_and_names_it()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows() && Environment.IsPrivilegedProcess, "a privileged user reads any folder");
        using var workspace = new BackendWorkspace();
        await workspace.AppStatus.CheckAsync(workspace.Token);
        var game = await workspace.AddGameAsync();
        await workspace.CreateProfileAsync(game);
        var unreadable = Path.Combine(workspace.DefaultStorageRoot, game.Id, "install.git", "objects");
        var destination = Path.Combine(workspace.Root, "new-storage");

        using (MakeUnreadable(unreadable))
        {
            Assert.SkipWhen(CanList(unreadable), "this file system ignores permission changes (a Windows drive mounted under WSL does)");
            var started = await workspace.Settings.RelocateStorageAsync(destination);

            Assert.Equal("error.storageRelocation.copyFailed", started.Error?.MessageId);
            Assert.Equal(unreadable, started.Error!.Args["path"]);
        }

        Assert.False(Directory.Exists(destination));
        Assert.Equal(workspace.DefaultStorageRoot, (await workspace.Settings.GetAsync(workspace.Token)).Value!.StorageRoot);
    }

    private static bool CanList(string folder)
    {
        try
        {
            _ = Directory.EnumerateFileSystemEntries(folder).Any();
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static IDisposable MakeUnreadable(string folder)
    {
        if (OperatingSystem.IsWindows())
        {
            return DenyListing(folder);
        }

        return RemoveAccess(folder);
    }

    [UnsupportedOSPlatform("windows")]
    private static Restore RemoveAccess(string folder)
    {
        var original = File.GetUnixFileMode(folder);
        File.SetUnixFileMode(folder, UnixFileMode.None);
        return new Restore(() => File.SetUnixFileMode(folder, original));
    }

    [SupportedOSPlatform("windows")]
    private static Restore DenyListing(string folder)
    {
        var info = new DirectoryInfo(folder);
        var rule = new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!, FileSystemRights.ListDirectory, AccessControlType.Deny);
        var security = info.GetAccessControl();
        security.AddAccessRule(rule);
        info.SetAccessControl(security);
        return new Restore(() =>
        {
            var restored = info.GetAccessControl();
            restored.RemoveAccessRule(rule);
            info.SetAccessControl(restored);
        });
    }

    private sealed class Restore(Action action) : IDisposable
    {
        public void Dispose() => action();
    }
}
