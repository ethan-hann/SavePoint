using Savepoint.Core.Paths;
using Savepoint.Core.Platform;
using Savepoint.Core.Platform.Linux;
using Savepoint.Core.Platform.Windows;
using Savepoint.Core.Tests.Paths;

namespace Savepoint.Core.Tests.Platform;

// GOG Galaxy's own folder and its default Games folder hold every GOG game, so neither may be tracked as one game, as for Steam.
public class GogFolderProtectionTests
{
    private const string _client = @"D:\Program Files (x86)\GOG Galaxy";
    private const string _games = @"D:\Program Files (x86)\GOG Galaxy\Games";

    private static FakeEnvironment Machine(bool withGalaxy = true)
    {
        var env = FakeEnvironment.Windows();
        if (withGalaxy)
        {
            env.GogFolders.AddRange([_client, _games]);
        }

        return env;
    }

    private static PathSafety Safety(PlatformEnvironment env) =>
        new(new WindowsPlatform(env), SafetyFixtures.StorageRootFor(OsKind.Windows), SafetyFixtures.ExtraRootsFor(OsKind.Windows), PathProbes.None);

    [Fact]
    public void Galaxys_program_folder_and_its_games_folder_are_tier_two()
    {
        var safety = Safety(Machine().Build());

        PathSafetyAssert.Blocked(safety, _client, 2);
        PathSafetyAssert.Blocked(safety, _games, 2);
    }

    [Fact]
    public void A_game_inside_the_games_folder_is_accepted()
    {
        var safety = Safety(Machine().Build());

        PathSafetyAssert.Accepted(safety, _games + @"\Deus Ex GOTY");
        PathSafetyAssert.Accepted(safety, _games + @"\Deus Ex GOTY\System");
    }

    [Fact]
    public void The_folders_are_protected_whatever_the_case_they_are_spelled_in()
    {
        var safety = Safety(Machine().Build());

        PathSafetyAssert.Blocked(safety, @"d:\program files (x86)\gog galaxy\games\", 2);
    }

    [Fact]
    public void Without_galaxy_nothing_is_added_to_the_table()
    {
        var baseline = new WindowsPlatform(Machine(withGalaxy: false).Build()).Denylist([]);
        var withGalaxy = new WindowsPlatform(Machine().Build()).Denylist([]);

        Assert.DoesNotContain(baseline.Entries, e => e.Path.Contains("GOG Galaxy", StringComparison.OrdinalIgnoreCase));
        Assert.All(withGalaxy.Entries.Where(e => e.Path.Contains("GOG Galaxy", StringComparison.OrdinalIgnoreCase)), e => Assert.Equal(DenylistTier.ExactOnly, e.Tier));
    }

    [Fact]
    public void A_folder_that_is_not_absolute_is_ignored()
    {
        var env = Machine(withGalaxy: false);
        env.GogFolders.AddRange(["GOG Galaxy", "..", ""]);

        var list = new WindowsPlatform(env.Build()).Denylist([]);
        var baseline = new WindowsPlatform(Machine(withGalaxy: false).Build()).Denylist([]);

        Assert.Equal(baseline.Entries.Count, list.Entries.Count);
    }

    [Fact]
    public void A_lookup_that_throws_never_breaks_the_table()
    {
        var env = Machine().Build() with { GetGogFolders = () => throw new IOException("registry gone") };
        var baseline = new WindowsPlatform(Machine(withGalaxy: false).Build()).Denylist([]);

        var list = new WindowsPlatform(env).Denylist([]);

        Assert.Equal(baseline.Entries.Count, list.Entries.Count);
    }

    [Fact]
    public void Other_systems_do_not_protect_galaxys_folders()
    {
        var env = FakeEnvironment.Linux();
        env.GogFolders.AddRange(["/opt/gog"]);

        var list = new LinuxPlatform(env.Build()).Denylist([]);

        Assert.DoesNotContain(list.Entries, e => e.Path == "/opt/gog");
    }
}
