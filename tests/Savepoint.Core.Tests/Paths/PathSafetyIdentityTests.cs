using Savepoint.Core.Paths;
using Savepoint.Core.Platform;
using Savepoint.Core.Tests.Platform;

namespace Savepoint.Core.Tests.Paths;

// Folders that two names reach (a SUBST drive, a mapped drive, a bind mount) look different to every name comparison.
// The fake probe gives each folder an identity, so the identity rules are checked on any host.
public class PathSafetyIdentityTests
{
    private static readonly PathSyntax _linux = PathSyntax.For(OsKind.Linux);

    [Fact]
    public void A_folder_inside_an_alias_of_a_tier_one_root_is_refused()
    {
        var safety = Safety(Probe().Identity("/x/alias/etc", 1, 100));

        PathSafetyAssert.Blocked(safety, "/x/alias/etc", 1);
        PathSafetyAssert.Blocked(safety, "/x/alias/etc/ssh", 1);
        PathSafetyAssert.Blocked(safety, "/x/alias/etc/new/deeper/folder", 1);
        PathSafetyAssert.Accepted(safety, "/x/alias/other");
    }

    [Fact]
    public void Without_identities_the_alias_is_only_what_its_name_says()
    {
        var safety = Safety(new FakePathProbe(_linux));

        PathSafetyAssert.Accepted(safety, "/x/alias/etc/ssh");
    }

    [Fact]
    public void An_alias_of_a_tier_two_root_is_refused_exactly_and_the_folders_inside_it_are_accepted()
    {
        var safety = Safety(Probe().Identity("/x/home-alias", 1, 201));

        PathSafetyAssert.Blocked(safety, "/x/home-alias", 2);
        PathSafetyAssert.Accepted(safety, "/x/home-alias/Games");
        PathSafetyAssert.Accepted(safety, "/x/home-alias/Games/Skyrim");
    }

    [Fact]
    public void An_alias_of_a_folder_that_holds_the_storage_root_is_refused()
    {
        var safety = Safety(Probe().Identity("/x/data-alias", 1, 300));

        PathSafetyAssert.Blocked(safety, "/x/data-alias", 1);
        PathSafetyAssert.Accepted(safety, "/x/data-alias/other");
    }

    [Fact]
    public void An_alias_of_a_folder_that_holds_a_storage_root_which_is_itself_a_link_is_refused()
    {
        var probe = Probe()
            .Link("/data/games/savepoint", "/mnt/big/data")
            .Identity("/data/games", 1, 650)
            .Identity("/mnt", 1, 610)
            .Identity("/mnt/big", 1, 660)
            .Identity("/mnt/big/data", 1, 700)
            .Identity("/data/games/savepoint", 1, 700)
            .Identity("/x/games-alias", 1, 650)
            .Identity("/x/big-alias", 1, 660);
        var safety = new PathSafety(FakePlatforms.For(OsKind.Linux), "/data/games/savepoint", probe: probe);

        PathSafetyAssert.Blocked(safety, "/x/games-alias", 1);
        PathSafetyAssert.Blocked(safety, "/x/big-alias", 1);
    }

    [Fact]
    public void A_tier_one_roots_exception_still_applies_when_the_folders_are_reached_by_alias()
    {
        var safety = Safety(Probe()
            .Identity("/x/run-alias", 1, 400)
            .Identity("/x/run-alias/media", 1, 401)
            .Identity("/x/run-alias/user", 1, 450));

        PathSafetyAssert.Accepted(safety, "/x/run-alias/media/test/USB/Games/G");
        PathSafetyAssert.Blocked(safety, "/x/run-alias/media", 2);
        PathSafetyAssert.Blocked(safety, "/x/run-alias/user/1000/app", 1);
        PathSafetyAssert.Blocked(safety, "/x/run-alias", 1);
    }

    [Fact]
    public void The_same_file_id_on_another_volume_is_a_different_folder()
    {
        var safety = Safety(Probe().Identity("/x/other-volume-etc", 2, 100));

        PathSafetyAssert.Accepted(safety, "/x/other-volume-etc");
        PathSafetyAssert.Accepted(safety, "/x/other-volume-etc/ssh");
    }

    [Theory]
    [InlineData("/x/alias/etc", "/etc", true)]
    [InlineData("/x/alias/etc/ssh", "/etc", true)]
    [InlineData("/etc", "/x/alias/etc/ssh", true)]
    [InlineData("/x/alias/etc", "/home", false)]
    [InlineData("/x/other-volume-etc", "/etc", false)]
    public void Overlap_follows_identities_as_well_as_names(string a, string b, bool expected)
    {
        var safety = Safety(Probe().Identity("/x/alias/etc", 1, 100).Identity("/x/other-volume-etc", 2, 100));

        Assert.Equal(expected, safety.PathsOverlap(a, b));
        Assert.Equal(expected, safety.PathsOverlap(b, a));
    }

    // The folders the Linux table names, each with an identity, plus /x as a place the aliases live.
    private static FakePathProbe Probe() => new FakePathProbe(_linux)
        .Identity("/", 1, 1)
        .Identity("/x", 1, 10)
        .Identity("/x/alias", 1, 11)
        .Identity("/etc", 1, 100)
        .Identity("/home", 1, 200)
        .Identity("/home/test", 1, 201)
        .Identity("/data", 1, 300)
        .Identity("/data/savepoint-storage", 1, 301)
        .Identity("/run", 1, 400)
        .Identity("/run/media", 1, 401);

    private static PathSafety Safety(IPathProbe probe) =>
        new(FakePlatforms.For(OsKind.Linux), "/data/savepoint-storage", probe: probe);
}
