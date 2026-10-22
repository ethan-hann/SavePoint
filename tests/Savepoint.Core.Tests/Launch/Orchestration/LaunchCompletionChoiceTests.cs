using Savepoint.Core.Api;

namespace Savepoint.Core.Tests.Launch.Orchestration;

// A session is completed only with a choice it offered, whatever the caller asks for (D31).
public class LaunchCompletionChoiceTests
{
    [Fact(Timeout = 120_000)]
    public async Task A_choice_the_session_did_not_offer_is_refused_before_any_capture_starts_and_the_session_still_waits()
    {
        using var rig = await LaunchRig.CreateAsync(gameArguments: ["--exit-after", "800"]);
        var vanilla = await rig.ProfileAsync("Vanilla");
        await rig.StartAsync(new LaunchTarget.ProfileTip(vanilla.Id));
        var exited = Assert.IsType<SessionState.Exited>((await rig.WaitForAsync<SessionState.Exited>()).State);
        Assert.DoesNotContain(CompletionChoice.MoveForward, exited.Options.Choices);
        var capturesBefore = rig.Jobs.List().Count(job => job.Kind == JobKind.Capture);

        var moved = await rig.Launch.CompleteSessionAsync(rig.Game.Id, new SessionCompletion.MoveForward("Moved on"));
        var forked = await rig.Launch.CompleteSessionAsync(rig.Game.Id, new SessionCompletion.ForkNewProfile("Branch", "Branch point"));

        Assert.Equal("error.launch.completionNotOffered", moved.Error?.MessageId);
        Assert.Equal("moveForward", moved.Error!.Args["choice"]);
        Assert.Equal("error.launch.completionNotOffered", forked.Error?.MessageId);
        Assert.Equal(capturesBefore, rig.Jobs.List().Count(job => job.Kind == JobKind.Capture));
        Assert.IsType<SessionState.Exited>(rig.Launch.GetSession(rig.Game.Id)!.State);

        // The choices it did offer still work.
        Assert.True((await rig.Launch.CompleteSessionAsync(rig.Game.Id, new SessionCompletion.CaptureNew("After play"))).Ok);
        await rig.WaitForAsync<SessionState.Completed>();
    }
}
