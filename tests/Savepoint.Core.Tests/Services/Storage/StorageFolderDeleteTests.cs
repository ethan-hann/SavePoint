using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using Savepoint.Core.Errors;
using Savepoint.Core.Services.Storage;
using Savepoint.Core.Tests.Backend;

namespace Savepoint.Core.Tests.Services.Storage;

// Deleting a folder in the storage fails for different reasons, and only a program holding a file is fixed by closing programs.
public class StorageFolderDeleteTests
{
    [Fact(Timeout = 60_000)]
    public async Task A_folder_that_access_is_denied_for_fails_with_its_own_error_and_not_the_file_in_use_one()
    {
        Assert.SkipWhen(!OperatingSystem.IsWindows() && Environment.IsPrivilegedProcess, "a privileged user ignores folder permissions");
        using var workspace = new BackendWorkspace();
        var storage = await workspace.Backend.Services.Storage.GetAsync(workspace.Token);
        var folder = Path.Combine(storage.Root, "stray-folder");
        var file = Path.Combine(folder, "inner", "file.txt");
        BackendWorkspace.WriteFile(file, "x");

        using (Deny(folder, file))
        {
            var error = await Assert.ThrowsAsync<StorageRemoveFailedException>(() => StorageFolders.DeleteAsync(storage, folder, workspace.Token));

            Assert.Equal("error.storage.removeFailed", error.MessageId);
            Assert.StartsWith(folder, (string)error.Args["path"]!, StringComparison.Ordinal);
        }

        Assert.True(File.Exists(file));
    }

    [Fact(Timeout = 60_000)]
    public async Task A_file_another_program_holds_open_still_fails_as_a_file_in_use()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "only Windows refuses to delete a file another program holds open");
        using var workspace = new BackendWorkspace();
        var storage = await workspace.Backend.Services.Storage.GetAsync(workspace.Token);
        var folder = Path.Combine(storage.Root, "stray-folder");
        var file = Path.Combine(folder, "file.txt");
        BackendWorkspace.WriteFile(file, "x");

        using (new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var error = await Assert.ThrowsAsync<GitOperationException>(() => StorageFolders.DeleteAsync(storage, folder, workspace.Token));

            Assert.Equal("error.gitOperation.checkoutFailed", error.MessageId);
        }
    }

    // Makes deleting the file fail with "access denied" until the returned object is disposed.
    private static IDisposable Deny(string folder, string file)
    {
        if (OperatingSystem.IsWindows())
        {
            return DenyOnWindows(Path.GetDirectoryName(file)!, file);
        }

        return RemoveWriteAccess(Path.GetDirectoryName(file)!);
    }

    [UnsupportedOSPlatform("windows")]
    private static Restore RemoveWriteAccess(string folder)
    {
        var original = File.GetUnixFileMode(folder);
        File.SetUnixFileMode(folder, UnixFileMode.UserRead | UnixFileMode.UserExecute);
        return new Restore(() => File.SetUnixFileMode(folder, original));
    }

    [SupportedOSPlatform("windows")]
    private static Restore DenyOnWindows(string folder, string file)
    {
        var user = WindowsIdentity.GetCurrent().User!;
        var folderInfo = new DirectoryInfo(folder);
        var fileInfo = new FileInfo(file);
        var folderRule = new FileSystemAccessRule(user, FileSystemRights.DeleteSubdirectoriesAndFiles, AccessControlType.Deny);
        var fileRule = new FileSystemAccessRule(user, FileSystemRights.Delete, AccessControlType.Deny);
        var folderSecurity = folderInfo.GetAccessControl();
        folderSecurity.AddAccessRule(folderRule);
        folderInfo.SetAccessControl(folderSecurity);
        var fileSecurity = fileInfo.GetAccessControl();
        fileSecurity.AddAccessRule(fileRule);
        fileInfo.SetAccessControl(fileSecurity);
        return new Restore(() =>
        {
            var restoredFile = fileInfo.GetAccessControl();
            restoredFile.RemoveAccessRule(fileRule);
            fileInfo.SetAccessControl(restoredFile);
            var restoredFolder = folderInfo.GetAccessControl();
            restoredFolder.RemoveAccessRule(folderRule);
            folderInfo.SetAccessControl(restoredFolder);
        });
    }

    private sealed class Restore(Action action) : IDisposable
    {
        public void Dispose() => action();
    }
}
