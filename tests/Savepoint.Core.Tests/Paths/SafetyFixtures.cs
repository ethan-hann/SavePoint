using Savepoint.Core.Paths;
using Savepoint.Core.Platform;
using Savepoint.Core.Tests.Platform;

namespace Savepoint.Core.Tests.Paths;

/// <summary>The fake machines the lexical path-safety tests share: one storage root that was moved off the default, and one caller-added root.</summary>
internal static class SafetyFixtures
{
    public static string StorageRootFor(OsKind os) => os switch
    {
        OsKind.Windows => @"D:\SavePointStorage",
        OsKind.Linux => "/data/savepoint-storage",
        _ => "/Volumes/Big/SavePointStorage",
    };

    public static string[] ExtraRootsFor(OsKind os) => os switch
    {
        OsKind.Windows => [@"E:\Extra\SavePoint"],
        OsKind.Linux => ["/srv/extra-savepoint"],
        _ => ["/Users/test/Extra/SavePoint"],
    };

    /// <summary>Everything a PathSafety built for <paramref name="os"/> must refuse as tier 1, written out here and not read from the code under test.</summary>
    public static string[] ProtectedRootsFor(OsKind os)
    {
        var directories = FakePlatforms.For(os).Directories;
        return [directories.CatalogDir, directories.LogsDir, directories.RuntimeDir, StorageRootFor(os), .. ExtraRootsFor(os)];
    }

    public static (PathSafety Safety, PathDenylist List, PathSyntax Syntax) Build(OsKind os, IPathProbe? probe = null)
    {
        var platform = FakePlatforms.For(os);
        var safety = new PathSafety(platform, StorageRootFor(os), ExtraRootsFor(os), probe ?? PathProbes.None);
        return (safety, platform.Denylist(ProtectedRootsFor(os)), PathSyntax.For(os));
    }
}
