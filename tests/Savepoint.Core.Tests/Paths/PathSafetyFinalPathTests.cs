using Savepoint.Core.Paths;
using Savepoint.Core.Platform;
using Savepoint.Core.Tests.Platform;

namespace Savepoint.Core.Tests.Paths;

// A folder in the middle of a protected tree can be reached through a name that has no protected folder on its own chain (SUBST,
// a mapped drive, a mounted volume, a link the resolver cannot read). The OS's final path for the deepest folder that exists is
// the one name that shows where it leads; the fake probe stands in for it so every rule runs on any host.
public class PathSafetyFinalPathTests
{
    private static readonly PathSyntax _windows = PathSyntax.For(OsKind.Windows);

    [Fact]
    public void A_folder_whose_final_path_is_inside_a_tier_one_root_is_refused_with_whatever_is_not_made_yet_appended()
    {
        var safety = Safety(Probe().FinalPath(@"S:\", @"C:\Windows\System32"));

        PathSafetyAssert.Blocked(safety, @"S:\", 1);
        PathSafetyAssert.Blocked(safety, @"S:\drivers", 1);
        PathSafetyAssert.Blocked(safety, @"S:\not\made\yet", 1);
        PathSafetyAssert.Blocked(safety, @"s:/drivers/..", 1);
    }

    [Fact]
    public void A_folder_whose_final_path_is_elsewhere_is_accepted()
    {
        var safety = Safety(Probe().FinalPath(@"T:\", @"D:\Games").FinalPath(@"T:\Skyrim", @"D:\Games\Skyrim"));

        PathSafetyAssert.Accepted(safety, @"T:\Skyrim");
        PathSafetyAssert.Accepted(safety, @"T:\Skyrim\Saves");
        PathSafetyAssert.Accepted(safety, @"T:\Other\Game");
    }

    [Fact]
    public void A_folder_whose_final_path_is_the_same_as_its_own_name_changes_nothing()
    {
        var safety = Safety(Probe().FinalPath(@"D:\Games\Skyrim", @"D:\Games\Skyrim").FinalPath(@"C:\Windows", @"C:\Windows"));

        PathSafetyAssert.Accepted(safety, @"D:\Games\Skyrim");
        PathSafetyAssert.Blocked(safety, @"C:\Windows", 1);
    }

    [Fact]
    public void The_deepest_existing_folder_decides_so_a_link_inside_a_protected_folder_that_leads_out_is_accepted()
    {
        var safety = Safety(Probe()
            .FinalPath(@"S:\", @"C:\Windows")
            .FinalPath(@"S:\Temp", @"C:\Windows\Temp")
            .FinalPath(@"S:\Temp\Games", @"D:\Games"));

        PathSafetyAssert.Blocked(safety, @"S:\Temp\Other", 1);
        PathSafetyAssert.Accepted(safety, @"S:\Temp\Games\Skyrim");
        PathSafetyAssert.Accepted(safety, @"S:\Temp\Games");
    }

    [Fact]
    public void A_folder_whose_final_path_is_a_tier_two_folder_is_refused_exactly_and_the_folders_inside_it_are_accepted()
    {
        var safety = Safety(Probe().FinalPath(@"D:\alias", @"C:\Users\Test"));

        PathSafetyAssert.Blocked(safety, @"D:\alias", 2);
        PathSafetyAssert.Accepted(safety, @"D:\alias\Games");
        PathSafetyAssert.Accepted(safety, @"D:\alias\Games\Skyrim");
    }

    [Fact]
    public void A_folder_whose_final_path_is_a_whole_drive_is_refused_as_a_volume()
    {
        var safety = Safety(Probe().FinalPath(@"D:\alias", @"E:\"));

        PathSafetyAssert.Blocked(safety, @"D:\alias", 2);
        PathSafetyAssert.Accepted(safety, @"D:\alias\Games");
    }

    [Fact]
    public void A_folder_whose_final_path_is_a_steam_library_folder_is_refused_exactly()
    {
        var safety = Safety(Probe().FinalPath(@"D:\library", @"E:\SteamLibrary\steamapps\common"));

        PathSafetyAssert.Blocked(safety, @"D:\library", 2);
        PathSafetyAssert.Accepted(safety, @"D:\library\Skyrim");
    }

    [Fact]
    public void A_folder_whose_final_path_holds_a_tier_one_root_is_refused()
    {
        var safety = Safety(Probe().FinalPath(@"D:\alias", @"E:\Extra"));

        PathSafetyAssert.Blocked(safety, @"D:\alias", 1);
        PathSafetyAssert.Accepted(safety, @"D:\alias\Other");
    }

    [Fact]
    public void A_tier_two_verdict_on_the_final_path_does_not_hide_a_tier_one_verdict_on_the_name()
    {
        var safety = Safety(Probe().FinalPath(@"C:\Windows\Alias", @"C:\Users\Test"));

        PathSafetyAssert.Blocked(safety, @"C:\Windows\Alias", 1);
    }

    [Fact]
    public void A_tier_one_verdict_on_the_final_path_wins_over_a_tier_two_verdict_on_the_name()
    {
        var safety = Safety(Probe().FinalPath(@"C:\Users\Test", @"C:\Windows\System32"));

        PathSafetyAssert.Blocked(safety, @"C:\Users\Test", 1);
    }

    [Fact]
    public void A_folder_with_several_final_names_is_refused_when_any_one_of_them_is_protected()
    {
        var safety = Safety(Probe().FinalPath(@"D:\mounted", @"D:\Volumes\Spare", @"C:\Windows\System32"));

        PathSafetyAssert.Blocked(safety, @"D:\mounted", 1);
        PathSafetyAssert.Blocked(safety, @"D:\mounted\game", 1);
    }

    [Fact]
    public void A_folder_with_several_final_names_none_of_them_protected_is_accepted()
    {
        var safety = Safety(Probe().FinalPath(@"D:\mounted", @"D:\Volumes\Spare", @"F:\Spare"));

        PathSafetyAssert.Accepted(safety, @"D:\mounted\game");
    }

    [Theory]
    [InlineData(@"\\testpc\share\games", 1)]
    [InlineData(@"\\TESTPC.corp.example\games", 1)]
    [InlineData(@"\\localhost\C$\Windows", 1)]
    [InlineData(@"\\127.0.0.1\games\Skyrim", 1)]
    [InlineData(@"\\nas\games\Skyrim", 0)]
    public void A_folder_whose_final_path_is_on_this_computers_own_network_names_is_refused(string finalPath, int tier)
    {
        var safety = Safety(Probe().FinalPath(@"Z:\", finalPath));

        if (tier == 0)
        {
            PathSafetyAssert.Accepted(safety, @"Z:\Skyrim");
        }
        else
        {
            PathSafetyAssert.Blocked(safety, @"Z:\Skyrim", tier);
        }
    }

    [Fact]
    public void A_folder_a_link_leads_to_through_this_computers_administrative_share_is_refused()
    {
        var safety = Safety(Probe().Link(@"D:\work\alias", @"\\nas\admin$\games"));

        PathSafetyAssert.Blocked(safety, @"D:\work\alias", 1);
    }

    [Theory]
    [InlineData("garbage")]
    [InlineData(@"relative\path")]
    [InlineData("")]
    [InlineData(@"\\?\Volume{00000000-0000-0000-0000-000000000000}\Windows")]
    public void A_final_name_that_is_not_an_absolute_path_is_ignored(string name)
    {
        var safety = Safety(Probe().FinalPath(@"D:\alias", name));

        PathSafetyAssert.Accepted(safety, @"D:\alias\Games");
    }

    [Fact]
    public void Names_are_compared_without_regard_to_case()
    {
        var safety = Safety(Probe().FinalPath(@"S:\", @"c:\WINDOWS\system32"));

        PathSafetyAssert.Blocked(safety, @"S:\x", 1);
    }

    [Theory]
    [InlineData(@"S:\game", @"C:\Windows\System32", true)]
    [InlineData(@"C:\Windows\System32", @"S:\game", true)]
    [InlineData(@"S:\game", @"C:\Windows", true)]
    [InlineData(@"S:\game", @"D:\Other", false)]
    [InlineData(@"S:\game", @"T:\Games\Skyrim", false)]
    public void Overlap_follows_final_paths_as_well_as_names(string a, string b, bool expected)
    {
        var safety = Safety(Probe().FinalPath(@"S:\", @"C:\Windows\System32").FinalPath(@"T:\", @"D:\Games"));

        Assert.Equal(expected, safety.PathsOverlap(a, b));
        Assert.Equal(expected, safety.PathsOverlap(b, a));
    }

    [Fact]
    public void Two_aliases_of_the_same_folder_overlap_through_their_final_paths()
    {
        var safety = Safety(Probe().FinalPath(@"T:\", @"D:\Games").FinalPath(@"U:\", @"D:\Games\Skyrim"));

        Assert.True(safety.PathsOverlap(@"T:\Skyrim", @"U:\Saves"));
        Assert.False(safety.PathsOverlap(@"T:\Fallout", @"U:\Saves"));
    }

    [Fact]
    public void Without_final_paths_the_alias_is_only_what_its_name_says()
    {
        var safety = Safety(Probe());

        PathSafetyAssert.Accepted(safety, @"S:\drivers");
    }

    private static FakePathProbe Probe() => new(_windows);

    private static PathSafety Safety(IPathProbe probe)
    {
        var os = OsKind.Windows;
        return new PathSafety(FakePlatforms.For(os), SafetyFixtures.StorageRootFor(os), SafetyFixtures.ExtraRootsFor(os), probe);
    }
}
