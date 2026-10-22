using Savepoint.Core.Api;

namespace Savepoint.Core.Tests.Launch.Orchestration;

// The launch time is a courtesy: if recording it fails, the game that started is still followed (D31).
public class LaunchRecordFailureTests
{
    [Fact(Timeout = 120_000)]
    public async Task A_catalog_that_cannot_be_written_after_the_game_started_does_not_fail_the_launch_or_lose_the_game()
    {
        using var rig = await LaunchRig.CreateAsync();
        var vanilla = await rig.ProfileAsync("Vanilla");
        var profiles = Path.Combine(rig.Workspace.CatalogDirectory, "profiles.json");
        File.SetAttributes(profiles, FileAttributes.ReadOnly);
        try
        {
            var launch = await rig.StartAsync(new LaunchTarget.ProfileTip(vanilla.Id));
            await rig.WaitForGameAsync();

            Assert.Equal(JobStatus.Succeeded, launch.Status);
            Assert.IsType<SessionState.Running>(rig.Launch.GetSession(rig.Game.Id)!.State);
            Assert.NotEmpty(rig.GameProcessIds());
        }
        finally
        {
            File.SetAttributes(profiles, FileAttributes.Normal);
        }
    }
}
