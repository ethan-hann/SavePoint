using Savepoint.Core.Api;
using Savepoint.Core.Git;
using Savepoint.Core.Launch;
using Savepoint.Testing;

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

    [Fact(Timeout = 120_000)]
    public async Task A_failed_folder_read_when_the_game_ends_is_logged_as_a_warning_with_the_error()
    {
        var log = new RecordingDiagnosticLog();
        var armed = false;
        using var rig = await LaunchRig.CreateAsync(options => options with
        {
            Log = log,
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
        var line = Assert.Single(log.Containing("Launch session for", rig.Game.Id, "an error nobody planned for"));
        Assert.Equal(LogLevel.Warning, line.Level);
        Assert.IsType<InvalidOperationException>(line.Exception);
    }

    [Fact(Timeout = 120_000)]
    public async Task A_failed_folder_read_still_says_that_the_named_program_never_appeared()
    {
        var armed = false;
        using var rig = await LaunchRig.CreateAsync(
            options => options with
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
            },
            gameArguments: ["--exit-after", "300"],
            processName: "never-running-" + Guid.NewGuid().ToString("N")[..6]);
        var vanilla = await rig.ProfileAsync("Vanilla");
        await rig.StartAsync(new LaunchTarget.ProfileTip(vanilla.Id));
        Volatile.Write(ref armed, true);

        var exited = await ExitedWithinAsync(rig, TimeSpan.FromSeconds(30));
        Volatile.Write(ref armed, false);

        Assert.True(exited, "the session never ended");
        Assert.Equal(SessionEndKind.NamedProcessNeverAppeared, Assert.IsType<SessionState.Exited>(rig.Launch.GetSession(rig.Game.Id)!.State).EndKind);
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
