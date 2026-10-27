using System.Diagnostics;
using Savepoint.Core.Paths;

namespace Savepoint.Core.Tests.Paths;

// The real identity read on whichever OS runs the test: every OS tells a folder from another one, whatever name leads to it.
public class HostIdentityTests
{
    [Fact]
    public void A_folder_and_a_link_to_it_have_one_identity_and_nothing_else_shares_it()
    {
        using var tree = new TempTree();
        var folder = tree.Dir("folder");
        var other = tree.Dir("other");
        var link = tree.DirectoryLink("link", folder);
        var file = tree.File("file.txt");

        var identity = PathProbes.Host.ReadIdentity(folder);
        var otherIdentity = PathProbes.Host.ReadIdentity(other);
        var fileIdentity = PathProbes.Host.ReadIdentity(file);

        // A read that fails is null, and null never equals an identity, so the NotEqual checks need a real value on both sides.
        Assert.NotNull(identity);
        Assert.NotNull(otherIdentity);
        Assert.NotNull(fileIdentity);
        Assert.Equal(identity, PathProbes.Host.ReadIdentity(link));
        Assert.Equal(identity, PathProbes.Host.ReadIdentity(folder + Path.DirectorySeparatorChar));
        Assert.NotEqual(identity, otherIdentity);
        Assert.NotEqual(identity, fileIdentity);
    }

    [Fact]
    public void On_Linux_the_identity_is_the_device_and_inode_the_system_reports()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux(), "Compares with the answer of the Linux stat program.");
        using var tree = new TempTree();
        var folder = tree.Dir("folder");
        var file = tree.File("file.txt");
        var paths = new List<string> { folder, file };

        // /proc is a mount of its own on every Linux, so its device differs from the temp folder's.
        if (Directory.Exists("/proc"))
        {
            paths.Add("/proc");
        }

        foreach (var path in paths)
        {
            var (device, inode) = StatDeviceAndInode(path, "-L", "-c", "%d %i");
            var identity = PathProbes.Host.ReadIdentity(path);

            Assert.NotNull(identity);
            Assert.Equal(inode, (ulong)identity.Value.FileId);
            Assert.Equal(LinuxDeviceNumber((ulong)device), identity.Value.VolumeSerial);
        }

        if (paths.Count == 3)
        {
            Assert.NotEqual(PathProbes.Host.ReadIdentity(folder)!.Value.VolumeSerial, PathProbes.Host.ReadIdentity("/proc")!.Value.VolumeSerial);
        }
    }

    [Fact]
    public void On_macOS_the_identity_is_the_device_and_inode_the_system_reports()
    {
        Assert.SkipUnless(OperatingSystem.IsMacOS(), "Compares with the answer of the macOS stat program.");
        using var tree = new TempTree();

        foreach (var path in new[] { tree.Dir("folder"), tree.File("file.txt"), "/" })
        {
            var (device, inode) = StatDeviceAndInode(path, "-L", "-f", "%d %i");
            var identity = PathProbes.Host.ReadIdentity(path);

            Assert.NotNull(identity);
            Assert.Equal(inode, (ulong)identity.Value.FileId);
            Assert.Equal(unchecked((uint)device), identity.Value.VolumeSerial);
        }
    }

    [Fact]
    public void A_path_that_does_not_exist_has_no_identity()
    {
        using var tree = new TempTree();
        var folder = tree.Dir("folder");

        Assert.Null(PathProbes.Host.ReadIdentity(Path.Combine(folder, "missing")));
        Assert.Null(PathProbes.Host.ReadIdentity(Path.Combine(tree.Root, "nowhere", "deeper")));
    }

    [Fact]
    public void A_folder_renamed_away_keeps_its_identity_and_the_new_folder_at_its_path_has_another()
    {
        using var tree = new TempTree();
        var folder = tree.Dir("folder");
        var identity = PathProbes.Host.ReadIdentity(folder);

        Directory.Move(folder, folder + "-moved");
        Directory.CreateDirectory(folder);

        var replacement = PathProbes.Host.ReadIdentity(folder);

        Assert.NotNull(identity);
        Assert.NotNull(replacement);
        Assert.Equal(identity, PathProbes.Host.ReadIdentity(folder + "-moved"));
        Assert.NotEqual(identity, replacement);
    }

    // The same number glibc's major() and minor() make from st_dev, which statx reports as two numbers.
    private static ulong LinuxDeviceNumber(ulong device)
    {
        var major = ((device >> 8) & 0xfff) | ((device >> 32) & ~0xfffUL);
        var minor = (device & 0xff) | ((device >> 12) & ~0xffUL);
        return (major << 32) | minor;
    }

    private static (long Device, ulong Inode) StatDeviceAndInode(string path, params string[] options)
    {
        var info = new ProcessStartInfo("stat") { RedirectStandardOutput = true, UseShellExecute = false };
        foreach (var option in options)
        {
            info.ArgumentList.Add(option);
        }

        info.ArgumentList.Add(path);
        using var process = Process.Start(info)!;
        var output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();

        Assert.Equal(0, process.ExitCode);
        var numbers = output.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return (long.Parse(numbers[0]), ulong.Parse(numbers[1]));
    }
}
