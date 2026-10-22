using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using Savepoint.Core.Api;

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

            Assert.Equal("error.storageRelocation.sourceUnreadable", started.Error?.MessageId);
            Assert.Equal(unreadable, started.Error!.Args["path"]);
        }

        Assert.False(Directory.Exists(destination));
        Assert.Equal(workspace.DefaultStorageRoot, (await workspace.Settings.GetAsync(workspace.Token)).Value!.StorageRoot);
    }

    [Fact]
    public async Task A_folder_that_cannot_be_opened_and_is_not_named_like_a_game_folder_does_not_stop_the_move()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows() && Environment.IsPrivilegedProcess, "a privileged user reads any folder");
        using var workspace = new BackendWorkspace();
        await workspace.AppStatus.CheckAsync(workspace.Token);
        var game = await workspace.AddGameAsync();
        await workspace.CreateProfileAsync(game);
        await workspace.WaitForIdleAsync(TimeSpan.FromSeconds(60));
        var stray = Path.Combine(workspace.DefaultStorageRoot, "lost+found");
        Directory.CreateDirectory(stray);
        var destination = Path.Combine(workspace.Root, "new-storage");

        using (MakeUnreadable(stray))
        {
            Assert.SkipWhen(CanList(stray), "this file system ignores permission changes (a Windows drive mounted under WSL does)");
            var preview = await workspace.Settings.GetRelocationPreviewAsync(destination, workspace.Token);
            var started = await workspace.Settings.RelocateStorageAsync(destination);
            var job = await workspace.WaitForJobAsync(started.Value!.JobId, TimeSpan.FromSeconds(120));

            Assert.Null(preview.Value!.BlockedBy);
            Assert.NotEqual(JobStatus.Failed, job.Status);
        }

        Assert.Equal(destination, (await workspace.Settings.GetAsync(workspace.Token)).Value!.StorageRoot);
        Assert.True(Directory.Exists(Path.Combine(destination, game.Id, "install.git")));
        Assert.True(Directory.Exists(stray));
    }

    [Fact]
    public async Task A_folder_named_like_a_game_folder_that_cannot_be_opened_stops_the_move_and_the_preview_names_it()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows() && Environment.IsPrivilegedProcess, "a privileged user reads any folder");
        using var workspace = new BackendWorkspace();
        await workspace.AppStatus.CheckAsync(workspace.Token);
        var game = await workspace.AddGameAsync();
        await workspace.CreateProfileAsync(game);
        await workspace.WaitForIdleAsync(TimeSpan.FromSeconds(60));
        var orphan = Path.Combine(workspace.DefaultStorageRoot, "orphan-game");
        Directory.CreateDirectory(orphan);
        var destination = Path.Combine(workspace.Root, "new-storage");

        using (MakeUnreadable(orphan))
        {
            Assert.SkipWhen(CanList(orphan), "this file system ignores permission changes (a Windows drive mounted under WSL does)");
            var preview = await workspace.Settings.GetRelocationPreviewAsync(destination, workspace.Token);
            var started = await workspace.Settings.RelocateStorageAsync(destination);

            Assert.True(preview.Ok, preview.Error?.Detail);
            Assert.Equal("error.storageRelocation.sourceUnreadable", preview.Value.BlockedBy?.MessageId);
            Assert.Equal(orphan, preview.Value.BlockedBy!.Args["path"]);
            Assert.Equal("error.storageRelocation.sourceUnreadable", started.Error?.MessageId);
        }

        Assert.False(Directory.Exists(destination));
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
