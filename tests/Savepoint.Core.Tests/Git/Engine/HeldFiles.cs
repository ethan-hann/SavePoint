using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using Savepoint.Core.Git;

namespace Savepoint.Core.Tests.Git.Engine;

// Files git cannot read, made on purpose: held open without sharing (Windows only, the one system that enforces it) or unreadable by
// permission (every system). With the index looking older than the files, git has to read each file again and lists the unreadable
// ones as changed, though nobody changed them.
internal static class HeldFiles
{
    // The kinds of file this system can make.
    public static TheoryData<HeldFileKind> Kinds =>
        OperatingSystem.IsWindows() ? [HeldFileKind.InUse, HeldFileKind.Unreadable] : [HeldFileKind.Unreadable];

    // The error id a switch over such a file stops with.
    public static string ErrorIdOf(HeldFileKind kind) =>
        kind == HeldFileKind.InUse ? "error.gitOperation.checkoutFailed" : "error.gitOperation.unreadableFile";

    // A file stamped no earlier than the index cannot be vouched for by its stamp, so git reads it again; this makes every file such a file.
    public static void MakeRacy(RepoRef repo) => File.SetLastWriteTimeUtc(Path.Combine(repo.GitDir, "index"), DateTime.UtcNow.AddHours(-1));

    // Holds the file until the returned handle is disposed; skips the test where this system cannot do it.
    public static IDisposable Hold(string file, HeldFileKind kind)
    {
        if (kind == HeldFileKind.InUse)
        {
            Assert.SkipUnless(OperatingSystem.IsWindows(), "only Windows refuses to read a file another program holds open");
            return new FileStream(file, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        }

        Assert.SkipWhen(!OperatingSystem.IsWindows() && Environment.IsPrivilegedProcess, "a privileged user reads any file");
        var hold = OperatingSystem.IsWindows() ? DenyReading(file) : RemoveAccess(file);
        try
        {
            using var _ = File.OpenRead(file);
        }
        catch (UnauthorizedAccessException)
        {
            return hold;
        }

        hold.Dispose();
        Assert.Skip("this file system ignores permission changes (a Windows drive mounted under WSL does)");
        return hold;
    }

    [UnsupportedOSPlatform("windows")]
    private static Restore RemoveAccess(string file)
    {
        var original = File.GetUnixFileMode(file);
        File.SetUnixFileMode(file, UnixFileMode.None);
        return new Restore(() => File.SetUnixFileMode(file, original));
    }

    [SupportedOSPlatform("windows")]
    private static Restore DenyReading(string file)
    {
        var info = new FileInfo(file);
        var rule = new FileSystemAccessRule(WindowsIdentity.GetCurrent().User!, FileSystemRights.ReadData, AccessControlType.Deny);
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
