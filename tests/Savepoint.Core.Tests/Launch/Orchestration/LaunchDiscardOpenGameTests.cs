using Savepoint.Core.Api;
using Savepoint.Core.Catalog;

namespace Savepoint.Core.Tests.Launch.Orchestration;

// A discard never runs under a game that is open, whatever state its session is in (D31).
public class LaunchDiscardOpenGameTests
{
    private static LaunchTarget Tip(Profile profile) => new LaunchTarget.ProfileTip(profile.Id);

    [Fact(Timeout = 120_000)]
    public async Task A_discard_is_refused_while_the_game_is_still_open_after_its_session_was_ended()
    {
        using var rig = await LaunchRig.CreateAsync();
        var vanilla = await rig.ProfileAsync("Vanilla");
        await rig.StartAsync(Tip(vanilla));
        await rig.WaitForGameAsync();
        Assert.True((await rig.Launch.MarkFinishedAsync(rig.Game.Id)).Ok);
        await rig.WaitForAsync<SessionState.Exited>();
        Assert.True((await rig.Launch.CompleteSessionAsync(rig.Game.Id, new SessionCompletion.Skip())).Ok);
        await rig.WaitForAsync<SessionState.Completed>();
        rig.WriteSave("slot1.sav", "written by the open game");

        var refused = await rig.Launch.DiscardChangesAsync(rig.Game.Id, RepoRole.External);

        Assert.Equal("error.gameService.gameRunning", refused.Error?.MessageId);
        Assert.Equal("written by the open game", rig.ReadSave("slot1.sav"));
    }

    [Fact(Timeout = 120_000)]
    public async Task A_discard_is_refused_while_a_session_waits_for_the_players_choice_and_allowed_once_it_is_over()
    {
        using var rig = await LaunchRig.CreateAsync(gameArguments: ["--exit-after", "800"]);
        var vanilla = await rig.ProfileAsync("Vanilla");
        await rig.StartAsync(Tip(vanilla));
        await rig.WaitForAsync<SessionState.Exited>();
        rig.WriteSave("slot1.sav", "played on");

        var refused = await rig.Launch.DiscardChangesAsync(rig.Game.Id, RepoRole.External);

        Assert.Equal("error.gameService.gameRunning", refused.Error?.MessageId);
        Assert.Equal("played on", rig.ReadSave("slot1.sav"));

        Assert.True((await rig.Launch.CompleteSessionAsync(rig.Game.Id, new SessionCompletion.Skip())).Ok);
        await rig.WaitForAsync<SessionState.Completed>();
        var allowed = await rig.Launch.DiscardChangesAsync(rig.Game.Id, RepoRole.External);

        Assert.True(allowed.Ok, allowed.Error?.Detail);
        Assert.Equal("save one", rig.ReadSave("slot1.sav"));
    }
}
