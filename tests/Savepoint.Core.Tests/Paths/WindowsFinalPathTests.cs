using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using Savepoint.Core.Paths;
using Savepoint.Core.Platform;
using Savepoint.Core.Tests.Platform;

namespace Savepoint.Core.Tests.Paths;

// An alias into the middle of a protected tree passes every name and identity comparison: no folder on its own chain is a protected
// root. Only the final path the OS reports shows where it really leads. Aliases are made only inside the temp tree, and the
// protected tree is a stand-in root there, never a real system folder.
public class WindowsFinalPathTests
{
    [Fact]
    public void A_junction_into_the_middle_of_a_protected_tree_is_refused_even_when_name_resolution_cannot_see_it()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Junctions and final paths are Windows features.");
        using var tree = new TempTree();
        var blocked = tree.Dir("blocked");
        var inner = tree.Dir("blocked", "inner", "sub");
        var games = tree.Dir("elsewhere", "games");
        var toInner = tree.DirectoryLink(Path.Combine("work", "to-inner"), Path.GetDirectoryName(inner)!);
        var toGames = tree.DirectoryLink(Path.Combine("work", "to-games"), games);
        var safety = BlindSafety(tree, blocked);

        PathSafetyAssert.Blocked(safety, toInner, 1);
        PathSafetyAssert.Blocked(safety, Path.Combine(toInner, "sub"), 1);
        PathSafetyAssert.Blocked(safety, Path.Combine(toInner, "sub", "game"), 1);
        PathSafetyAssert.Blocked(safety, Path.Combine(toInner, "not", "made", "yet"), 1);
        PathSafetyAssert.Accepted(safety, Path.Combine(toGames, "Skyrim"));
    }

    [Fact]
    public void A_subst_drive_onto_a_folder_inside_a_protected_tree_is_refused()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "SUBST is a Windows feature.");
        using var tree = new TempTree();
        var blocked = tree.Dir("blocked");
        var inner = tree.Dir("blocked", "inner");
        var drive = WindowsTools.TrySubst(inner);
        Assert.SkipWhen(drive is null, "No free drive letter could be mapped with SUBST.");

        try
        {
            var safety = new PathSafety(FakePlatforms.ForHost(tree.Dir("home")), Path.Combine(tree.Root, "own-storage"), [blocked]);

            PathSafetyAssert.Blocked(safety, drive + @"\", 1);
            PathSafetyAssert.Blocked(safety, drive + @"\game", 1);
            PathSafetyAssert.Blocked(safety, drive + @"\not\made\yet", 1);
            Assert.True(safety.PathsOverlap(drive + @"\game", blocked));
        }
        finally
        {
            WindowsTools.RemoveSubst(drive!);
        }
    }

    [Fact]
    public void The_final_path_of_a_folder_is_its_own_name_in_the_case_it_has_on_disk()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Final paths come from the Windows API.");
        using var tree = new TempTree();
        var folder = tree.Dir("Mixed Case Folder", "Inner");
        var asTyped = folder.ToLowerInvariant();

        var names = PathProbes.Host.ReadFinalPaths(asTyped);

        Assert.Equal([folder], names);
        Assert.Equal([folder], PathProbes.Host.ReadFinalPaths(folder + @"\"));
        Assert.Equal([folder], PathProbes.Host.ReadFinalPaths(@"\\?\" + folder));
    }

    [Fact]
    public void The_final_path_of_a_file_is_the_file()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Final paths come from the Windows API.");
        using var tree = new TempTree();
        var file = tree.File(Path.Combine("sub", "Readme.TXT"));

        Assert.Equal([file], PathProbes.Host.ReadFinalPaths(file.ToLowerInvariant()));
    }

    [Fact]
    public void The_final_path_of_something_that_does_not_exist_is_empty()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Final paths come from the Windows API.");
        using var tree = new TempTree();

        Assert.Empty(PathProbes.Host.ReadFinalPaths(Path.Combine(tree.Root, "missing")));
        Assert.Empty(PathProbes.Host.ReadFinalPaths(Path.Combine(tree.Root, "nowhere", "deeper")));
    }

    [Fact]
    public void The_final_path_of_a_junction_and_of_a_folder_beneath_it_is_the_folder_it_leads_to()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Junctions and final paths are Windows features.");
        using var tree = new TempTree();
        var target = tree.Dir("target", "Inner");
        var junction = tree.DirectoryLink(Path.Combine("work", "to-target"), Path.GetDirectoryName(target)!);

        Assert.Equal([Path.GetDirectoryName(target)!], PathProbes.Host.ReadFinalPaths(junction));
        Assert.Equal([target], PathProbes.Host.ReadFinalPaths(Path.Combine(junction, "Inner")));
    }

    [Fact]
    public void The_final_path_of_a_long_path_is_not_cut_short()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Final paths come from the Windows API.");
        using var tree = new TempTree();
        var deep = tree.Dir(Enumerable.Repeat("a-folder-name-of-some-length", 12).ToArray());

        Assert.True(deep.Length > 260, "the test path must be longer than the classic limit");
        Assert.Equal([deep], PathProbes.Host.ReadFinalPaths(deep));
    }

    [Fact]
    public void The_final_path_of_a_subst_drive_is_the_folder_it_stands_for()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "SUBST is a Windows feature.");
        using var tree = new TempTree();
        var inner = tree.Dir("blocked", "inner");
        var drive = WindowsTools.TrySubst(inner);
        Assert.SkipWhen(drive is null, "No free drive letter could be mapped with SUBST.");

        try
        {
            Assert.Equal([inner], PathProbes.Host.ReadFinalPaths(drive + @"\"));
            Assert.Equal([inner], PathProbes.Host.ReadFinalPaths(drive));
            var game = Directory.CreateDirectory(Path.Combine(inner, "game")).FullName;
            Assert.Equal([game], PathProbes.Host.ReadFinalPaths(drive + @"\game"));
        }
        finally
        {
            WindowsTools.RemoveSubst(drive!);
        }
    }

    [Fact]
    public void The_final_path_of_the_loopback_admin_share_is_a_network_path()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Administrative shares are a Windows feature.");
        const string share = @"\\localhost\C$\Windows";
        Assert.SkipUnless(Directory.Exists(share), "The administrative share is not reachable from this account.");

        var names = PathProbes.Host.ReadFinalPaths(share);

        Assert.Single(names);
        Assert.StartsWith(@"\\", names[0]);
        Assert.EndsWith(@"\C$\Windows", names[0], StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(@"\\?\", names[0]);
    }

    [Fact]
    public void A_volume_guid_path_is_named_by_every_folder_the_volume_is_mounted_in()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Volume names are a Windows feature.");
        using var tree = new TempTree();
        var drive = Path.GetPathRoot(tree.Root)!;
        var guid = VolumeNameOf(drive);
        Assert.SkipWhen(guid is null, "The temp volume has no volume GUID name.");

        Assert.Equal([drive + @"dir\file"], WindowsFiles.MountedAs(guid + @"dir\file"));
        Assert.Equal([drive], WindowsFiles.MountedAs(guid));
        Assert.Empty(WindowsFiles.MountedAs(@"\\?\Volume{00000000-0000-0000-0000-000000000000}\dir"));
        Assert.Empty(WindowsFiles.MountedAs(@"C:\dir"));
        Assert.Empty(WindowsFiles.MountedAs(@"\\?\Volume{unterminated"));
    }

    [Fact]
    public void A_final_path_is_asked_for_by_drive_letter_first_and_by_volume_guid_when_the_volume_has_no_letter()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Volume names are a Windows feature.");
        using var tree = new TempTree();
        var drive = Path.GetPathRoot(tree.Root)!;
        var guid = VolumeNameOf(drive);
        Assert.SkipWhen(guid is null, "The temp volume has no volume GUID name.");
        var asked = new List<WindowsFiles.VolumeNameKind>();

        var byLetter = WindowsFiles.NamesOf(kind =>
        {
            asked.Add(kind);
            return @"\\?\C:\Games\Skyrim";
        });
        var byGuid = WindowsFiles.NamesOf(kind => kind == WindowsFiles.VolumeNameKind.Dos ? null : guid + @"Games\Skyrim");
        var byUnc = WindowsFiles.NamesOf(_ => @"\\?\UNC\nas\games\Skyrim");

        Assert.Equal([@"C:\Games\Skyrim"], byLetter);
        Assert.Equal([WindowsFiles.VolumeNameKind.Dos], asked);
        Assert.Equal([drive + @"Games\Skyrim"], byGuid);
        Assert.Equal([@"\\nas\games\Skyrim"], byUnc);
        Assert.Empty(WindowsFiles.NamesOf(_ => null));
        Assert.Empty(WindowsFiles.NamesOf(kind => kind == WindowsFiles.VolumeNameKind.Dos ? null : @"\\?\Volume{00000000-0000-0000-0000-000000000000}\x"));
    }

    // Mounting a volume in a folder needs an elevated account, so this runs only where someone already did it.
    [Fact]
    public void A_volume_mounted_in_a_folder_has_a_final_path_that_names_the_volume()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Mounted-folder volumes are a Windows feature.");
        var folder = FolderMountPoints().FirstOrDefault(Directory.Exists);
        Assert.SkipWhen(folder is null, "No volume is mounted in a folder on this machine, and mounting one needs elevation.");

        var names = PathProbes.Host.ReadFinalPaths(folder!);

        Assert.NotEmpty(names);
        Assert.All(names, name => Assert.True(PathSyntax.For(OsKind.Windows).IsAbsolute(name), name));
    }

    [Fact]
    public void The_legacy_identity_of_a_folder_agrees_with_its_full_identity()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "File identities come from the Windows API.");
        using var tree = new TempTree();
        var folder = tree.Dir("folder");
        var full = PathProbes.Host.ReadIdentity(folder);
        Assert.SkipWhen(full is null || (full.Value.FileId >> 64) != 0, "This volume has 128-bit file ids, which the 64-bit form cannot compare.");

        var legacy = WindowsFiles.ReadLegacyIdentity(folder);

        Assert.NotNull(legacy);
        Assert.Equal(full!.Value.FileId, legacy!.Value.FileId);
        Assert.Equal(full.Value.VolumeSerial & 0xFFFFFFFF, legacy.Value.VolumeSerial);
    }

    // mountvol lists every volume with its mount points without elevation; a mount point longer than a drive root is a folder.
    private static IEnumerable<string> FolderMountPoints()
    {
        if (!OperatingSystem.IsWindows())
        {
            return [];
        }

        var info = new ProcessStartInfo("mountvol") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
        using var process = Process.Start(info)!;
        var listing = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        return [.. listing.Split('\n').Select(line => line.Trim()).Where(line => Regex.IsMatch(line, @"^[A-Za-z]:\\.+"))];
    }

    private static string? VolumeNameOf(string driveRoot)
    {
        var buffer = new char[64];
        return GetVolumeNameForVolumeMountPoint(driveRoot, buffer, (uint)buffer.Length) ? new string(buffer).TrimEnd('\0') : null;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "GetVolumeNameForVolumeMountPointW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumeNameForVolumeMountPoint(string mountPoint, char[] volumeName, uint bufferLength);

    private static PathSafety BlindSafety(TempTree tree, string blocked) =>
        new(FakePlatforms.ForHost(tree.Dir("home")), Path.Combine(tree.Root, "own-storage"), [blocked], new LinkBlindProbe(PathProbes.Host));
}
