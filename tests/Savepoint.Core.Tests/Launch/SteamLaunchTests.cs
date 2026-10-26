using Savepoint.Core.Launch;
using Savepoint.Core.Platform;

namespace Savepoint.Core.Tests.Launch;

public class SteamLaunchTests
{
    private const string _reaper = "/home/u/.steam/ubuntu12_32/reaper SteamLaunch AppId=240 -- /home/u/entry-point --verb=waitforexitandrun";

    [Theory]
    [InlineData("steam://run/240", 240)]
    [InlineData("steam://rungameid/2379780", 2379780)]
    [InlineData(" STEAM://rungameid/240/ ", 240)]
    [InlineData("steam://run/240?x=1", 240)]
    public void A_Steam_run_link_names_its_app_id(string uri, int expected) => Assert.Equal(expected, SteamLaunch.AppIdOf(uri));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("steam://open/games")]
    [InlineData("steam://run/abc")]
    [InlineData("com.epicgames.launcher://apps/x?action=launch")]
    [InlineData("steam://run/99999999999")]
    public void Any_other_link_names_none(string? uri) => Assert.Null(SteamLaunch.AppIdOf(uri));

    [Fact]
    public void A_reaper_started_for_the_app_is_the_launch_helper()
    {
        Assert.True(SteamLaunch.IsLaunchHelper(new RunningProcess(5, "reaper", null, CommandLine: _reaper), 240));
    }

    [Fact]
    public void A_reaper_for_the_app_ending_the_line_is_the_launch_helper()
    {
        Assert.True(SteamLaunch.IsLaunchHelper(new RunningProcess(5, "reaper", null, CommandLine: "/x/reaper SteamLaunch AppId=240"), 240));
    }

    [Fact]
    public void A_reaper_for_another_app_whose_id_starts_the_same_is_not()
    {
        Assert.False(SteamLaunch.IsLaunchHelper(new RunningProcess(5, "reaper", null, CommandLine: "/x/reaper SteamLaunch AppId=2400 -- y"), 240));
        Assert.False(SteamLaunch.IsLaunchHelper(new RunningProcess(5, "reaper", null, CommandLine: "/x/reaper SteamLaunch AppId=1240 -- y"), 240));
    }

    [Fact]
    public void A_process_that_only_mentions_the_text_is_not_the_launch_helper()
    {
        Assert.False(SteamLaunch.IsLaunchHelper(new RunningProcess(5, "grep", null, CommandLine: "grep SteamLaunch AppId=240"), 240));
        Assert.False(SteamLaunch.IsLaunchHelper(new RunningProcess(5, "reaper", null), 240));
    }
}
