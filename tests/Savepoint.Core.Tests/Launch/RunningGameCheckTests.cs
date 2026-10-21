using Savepoint.Core.Launch;
using Savepoint.Core.Platform;

namespace Savepoint.Core.Tests.Launch;

public class RunningGameCheckTests
{
    private static readonly RunningProcess _game = new(500, "game", null);

    private static RunningGameCheck Check(ScriptedInspector processes, OsKind os = OsKind.Windows) => new(processes, os);

    [Fact]
    public void A_listed_process_that_matches_the_name_is_running()
    {
        var processes = new ScriptedInspector();
        processes.Set(ScriptedInspector.Bystander, _game);

        Assert.True(Check(processes).IsRunning("Game.exe"));
    }

    [Fact]
    public void A_process_list_without_a_match_says_not_running()
    {
        var processes = new ScriptedInspector();

        Assert.False(Check(processes).IsRunning("Game.exe"));
    }

    [Fact]
    public void An_unreadable_list_says_not_running_rather_than_guessing()
    {
        var processes = new ScriptedInspector();
        processes.Unreadable();

        Assert.False(Check(processes).IsRunning("Game.exe"));
    }

    [Fact]
    public void An_inspector_that_throws_says_not_running()
    {
        var processes = new ScriptedInspector { Throws = true };

        Assert.False(Check(processes).IsRunning("Game.exe"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void A_blank_name_matches_nothing_and_does_not_read_the_list(string name)
    {
        var processes = new ScriptedInspector();
        processes.Set(ScriptedInspector.Bystander, _game);

        Assert.False(Check(processes).IsRunning(name));
        Assert.Equal(0, processes.Calls);
    }

    [Fact]
    public void Names_are_compared_the_way_the_os_compares_them()
    {
        var processes = new ScriptedInspector();
        processes.Set(ScriptedInspector.Bystander, new RunningProcess(500, "Game", null));

        Assert.True(Check(processes, OsKind.Windows).IsRunning("game"));
        Assert.True(Check(processes, OsKind.MacOS).IsRunning("game"));
        Assert.False(Check(processes, OsKind.Linux).IsRunning("game"));
    }
}
