using System.Diagnostics;
using System.Runtime.InteropServices;
using Savepoint.Core.Paths;
using Savepoint.Core.Tests.Platform;

namespace Savepoint.Core.Tests.Paths;

// The real Windows identity calls. Links, SUBST drives and 8.3 names are made only inside the temp tree.
public class WindowsIdentityTests
{
    [Fact]
    public void A_folder_its_junction_and_its_short_name_have_one_identity_and_nothing_else_shares_it()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "File identities come from the Windows API.");
        using var tree = new TempTree();
        var folder = tree.Dir("folder");
        var other = tree.Dir("other");
        var junction = tree.DirectoryLink("junction", folder);
        var file = tree.File("file.txt");
        var longName = tree.Dir("LongDirectoryNameForShortNames");
        var shortName = ShortPathOf(longName);

        var identity = PathProbes.Host.ReadIdentity(folder);

        Assert.NotNull(identity);
        Assert.Equal(identity, PathProbes.Host.ReadIdentity(junction));
        Assert.Equal(identity, PathProbes.Host.ReadIdentity(folder + @"\"));
        Assert.NotEqual(identity, PathProbes.Host.ReadIdentity(other));
        Assert.NotEqual(identity, PathProbes.Host.ReadIdentity(file));
        Assert.NotNull(PathProbes.Host.ReadIdentity(file));
        Assert.Equal(PathProbes.Host.ReadIdentity(longName), PathProbes.Host.ReadIdentity(shortName));
        Assert.Null(PathProbes.Host.ReadIdentity(Path.Combine(folder, "missing")));
        Assert.Null(PathProbes.Host.ReadIdentity(Path.Combine(tree.Root, "nowhere", "deeper")));
    }

    [Fact]
    public void Identity_reads_a_drive_root_a_network_form_and_a_long_path()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "File identities come from the Windows API.");
        using var tree = new TempTree();
        var deep = tree.Dir(Enumerable.Repeat("a-folder-name-of-some-length", 12).ToArray());
        var root = Path.GetPathRoot(tree.Root)!;

        Assert.NotNull(PathProbes.Host.ReadIdentity(root));
        Assert.NotNull(PathProbes.Host.ReadIdentity(deep));
        Assert.Equal(PathProbes.Host.ReadIdentity(tree.Root), PathProbes.Host.ReadIdentity(@"\\?\" + tree.Root));
    }

    [Fact]
    public void A_junction_that_name_resolution_cannot_see_is_still_refused_through_identity()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Junctions and file identities are Windows features.");
        using var tree = new TempTree();
        var parent = tree.Dir("parent");
        var home = tree.Dir("home");
        var blocked = tree.Dir("blocked");
        var storage = Path.Combine(parent, "storage");
        var toBlocked = tree.DirectoryLink(Path.Combine("work", "to-blocked"), blocked);
        var toHome = tree.DirectoryLink(Path.Combine("work", "to-home"), home);
        var toParent = tree.DirectoryLink(Path.Combine("work", "to-parent"), parent);
        var platform = FakePlatforms.ForHost(home);
        var ownStorage = Path.Combine(tree.Root, "own-storage");

        var blind = new PathSafety(platform, ownStorage, [blocked, storage], new LinkBlindProbe(PathProbes.Host));
        var nameOnly = new PathSafety(platform, ownStorage, [blocked, storage], new LinkBlindProbe(PathProbes.None));

        PathSafetyAssert.Blocked(blind, Path.Combine(toBlocked, "inner", "game"), 1);
        PathSafetyAssert.Blocked(blind, toHome, 2);
        PathSafetyAssert.Accepted(blind, Path.Combine(toHome, "Games"));
        PathSafetyAssert.Blocked(blind, toParent, 1);
        PathSafetyAssert.Accepted(blind, Path.Combine(toParent, "elsewhere"));

        // The same junctions pass when neither names nor identities can tell: it is the identity that refuses them.
        PathSafetyAssert.Accepted(nameOnly, Path.Combine(toBlocked, "inner", "game"));
        PathSafetyAssert.Accepted(nameOnly, toParent);
    }

    [Fact]
    public void A_subst_drive_onto_a_protected_folder_is_refused()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "SUBST is a Windows feature.");
        using var tree = new TempTree();
        var home = tree.Dir("home");
        var blocked = tree.Dir("blocked");
        var parent = tree.Dir("parent");
        tree.Dir("games");
        var drive = TrySubst(tree.Root);
        Assert.SkipWhen(drive is null, "No free drive letter could be mapped with SUBST.");

        try
        {
            var safety = new PathSafety(FakePlatforms.ForHost(home), Path.Combine(tree.Root, "own-storage"), [blocked, Path.Combine(parent, "storage")]);

            PathSafetyAssert.Blocked(safety, drive + @"\blocked\game", 1);
            PathSafetyAssert.Blocked(safety, drive + @"\home", 2);
            PathSafetyAssert.Blocked(safety, drive + @"\parent", 1);
            PathSafetyAssert.Blocked(safety, drive + @"\", 2);
            PathSafetyAssert.Accepted(safety, drive + @"\games\Skyrim");
            Assert.True(safety.PathsOverlap(drive + @"\blocked", blocked));
        }
        finally
        {
            Run("subst", drive!, "/d");
        }
    }

    [Fact]
    public void The_loopback_admin_share_reports_the_identity_of_the_folder_it_shows()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Administrative shares are a Windows feature.");
        const string share = @"\\localhost\C$\Windows";
        Assert.SkipUnless(Directory.Exists(share), "The administrative share is not reachable from this account.");

        Assert.Equal(PathProbes.Host.ReadIdentity(@"C:\Windows"), PathProbes.Host.ReadIdentity(share));
    }

    [Fact]
    public void A_loopback_admin_share_is_refused_by_name_and_not_left_to_identity()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Administrative shares are a Windows feature.");
        using var tree = new TempTree();
        var safety = new PathSafety(FakePlatforms.ForHost(tree.Dir("home")), Path.Combine(tree.Root, "own-storage"));

        PathSafetyAssert.Blocked(safety, @"\\localhost\C$\Windows\System32", 1);
        PathSafetyAssert.Blocked(safety, @"\\127.0.0.1\ADMIN$\System32", 1);
    }

    private static string? TrySubst(string path)
    {
        for (var letter = 'Z'; letter >= 'D'; letter--)
        {
            var drive = $"{letter}:";
            if (!Directory.Exists(drive + @"\") && Run("subst", drive, path) == 0)
            {
                return drive;
            }
        }

        return null;
    }

    private static int Run(string command, params string[] arguments)
    {
        var info = new ProcessStartInfo(command) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments)
        {
            info.ArgumentList.Add(argument);
        }

        using var process = Process.Start(info)!;
        process.StandardOutput.ReadToEnd();
        process.StandardError.ReadToEnd();
        process.WaitForExit();
        return process.ExitCode;
    }

    private static string ShortPathOf(string path)
    {
        var buffer = new char[1024];
        var length = GetShortPathName(path, buffer, (uint)buffer.Length);
        return length == 0 ? path : new string(buffer, 0, (int)length);
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetShortPathName(string longPath, char[] shortPath, uint bufferLength);

    // Hides links from name resolution but keeps the real file-system identities.
    private sealed class LinkBlindProbe(IPathProbe inner) : IPathProbe
    {
        public string? ReadLinkTarget(string path) => null;

        public string? ExpandShortName(string path) => inner.ExpandShortName(path);

        public FileIdentity? ReadIdentity(string path) => inner.ReadIdentity(path);
    }
}
