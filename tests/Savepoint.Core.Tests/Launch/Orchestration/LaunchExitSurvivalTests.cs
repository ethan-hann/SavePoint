using Savepoint.Core.Api;
using Savepoint.Core.Git;

namespace Savepoint.Core.Tests.Launch.Orchestration;

// However reading the folders fails when the game ends, the session still ends (D31).
public class LaunchExitSurvivalTests
{
    [Fact(Timeout = 120_000)]
    public async Task An_unexpected_failure_reading_the_folders_when_the_game_ends_still_moves_the_session_to_exited_with_the_basic_choices()
    {
        var armed = false;
        using var rig = await LaunchRig.CreateAsync(options => options with
        {
            Engine = new GitEngineOptions
            {
                InvocationObserver = invocation =>
                {
                    if (Volatile.Read(ref armed) && invocation.Arguments[0] == "ls-files")
                    {
                        throw new InvalidOperationException("an error nobody planned for");
                    }
                },
            },
        });
        var vanilla = await rig.ProfileAsync("Vanilla");
        await rig.StartAsync(new LaunchTarget.ProfileTip(vanilla.Id));
        await rig.WaitForGameAsync();
        Volatile.Write(ref armed, true);

        Assert.True((await rig.Launch.MarkFinishedAsync(rig.Game.Id)).Ok);
        var exited = await ExitedWithinAsync(rig, TimeSpan.FromSeconds(15));
        Volatile.Write(ref armed, false);

        Assert.True(exited, "the session was still playing fifteen seconds after the player said they were done");
        var state = Assert.IsType<SessionState.Exited>(rig.Launch.GetSession(rig.Game.Id)!.State);
        Assert.Contains(CompletionChoice.Skip, state.Options.Choices);
        Assert.Equal(0, state.NewFileCount);
    }

    private static async Task<bool> ExitedWithinAsync(LaunchRig rig, TimeSpan limit)
    {
        var until = DateTime.UtcNow + limit;
        while (DateTime.UtcNow < until)
        {
            if (rig.Launch.GetSession(rig.Game.Id)?.State is SessionState.Exited)
            {
                return true;
            }

            await Task.Delay(50, rig.Token);
        }

        return false;
    }
}
