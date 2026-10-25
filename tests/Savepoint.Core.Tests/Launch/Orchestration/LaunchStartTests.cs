using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Launch;
using Savepoint.Core.Tests.Services.Exe;

namespace Savepoint.Core.Tests.Launch.Orchestration;

// Starting a launch: both folders are checked and switched, and the real stand-in game is started and followed.
public class LaunchStartTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static LaunchTarget Tip(Profile profile) => new LaunchTarget.ProfileTip(profile.Id);

    [Fact(Timeout = 180_000)]
    public async Task A_launch_switches_both_folders_starts_the_game_and_reports_every_phase_in_order()
    {
        var (rig, vanilla, _) = await LaunchRig.WithTwoProfilesAsync(gameArguments: ["--save-every", "150", "--exit-after", "1500"]);
        using var _ = rig;

        var job = await rig.StartAsync(Tip(vanilla));
        var exited = await rig.WaitForAsync<SessionState.Exited>();

        Assert.Equal(JobStatus.Succeeded, job.Status);
        Assert.Equal(JobKind.Launch, job.Kind);
        Assert.Equal(rig.Game.Id, job.GameId);

        // Both folders were put back to Vanilla before the game started.
        Assert.False(rig.InstallHas("mods/a.txt"));
        Assert.Equal("base content", rig.ReadInstall("data/base.pak"));
        Assert.False(rig.SavesHave("slot2.sav"));
        await rig.WaitForEventAsync(session => session.State is SessionState.Exited, "Exited");
        Assert.Equal(["Preparing", "Switching", "Running", "Exited"], rig.Phases());
        Assert.All(rig.Events, session =>
        {
            Assert.Equal(job.JobId, session.LaunchJobId);
            Assert.Equal(vanilla.Id, session.ProfileId);
            Assert.Equal(Tip(vanilla), session.Target);
            Assert.False(session.StartedFromOlderPoint);
        });
        Assert.Equal(rig.Events.Select(session => session.Revision).Order(), rig.Events.Select(session => session.Revision));
        Assert.Equal(rig.Events.Count, rig.Events.Select(session => session.Revision).Distinct().Count());

        var state = Assert.IsType<SessionState.Exited>(exited.State);
        Assert.True(state.NewFileCount >= 1, "the game wrote saves");
        Assert.True(state.PlayDuration > TimeSpan.Zero);
        Assert.Equal(0, state.ChangedInstallFileCount);

        // Modded was made from Vanilla's saves, so Vanilla's newest snapshot is Modded's too and cannot be replaced.
        Assert.Equal([CompletionChoice.CaptureNew, CompletionChoice.Skip], state.Options.Choices);
        Assert.Equal(CompletionChoice.CaptureNew, state.Options.Default);
        Assert.Null(state.Options.UpdateTarget);
        Assert.Equal(0, state.Options.NewerPointCount);
    }

    [Fact(Timeout = 240_000)]
    public async Task A_running_session_is_tracked_counted_and_keeps_the_game_taken_until_it_is_over()
    {
        var (rig, vanilla, modded) = await LaunchRig.WithTwoProfilesAsync();
        using var _ = rig;
        var services = rig.Workspace.Backend.Services;
        var changes = 0;
        services.Sessions.Changed += (_, _) => Interlocked.Increment(ref changes);

        // Two launches at the same moment, neither needing a switch: only one goes through.
        var both = await Task.WhenAll(rig.Launch.StartAsync(rig.Game.Id, Tip(modded)), rig.Launch.StartAsync(rig.Game.Id, Tip(modded)));
        Assert.Single(both, result => result.Ok);
        Assert.Single(both, result => result.Error?.MessageId == "error.launch.alreadyRunning");
        var job = await rig.WaitForJobAsync(both.Single(result => result.Ok).Value!.JobId);
        await rig.WaitForGameAsync();

        Assert.Equal(JobStatus.Succeeded, job.Status);
        await rig.WaitForEventAsync(session => session.State is SessionState.Running, "Running");
        Assert.Equal(["Preparing", "Running"], rig.Phases());

        // The services see the session: the profile is marked running, the launch is recorded, destructive work is refused (D13.5).
        Assert.True(changes > 0, "the services are told when a session opens");
        Assert.True(services.Sessions.IsGameRunning(rig.Game.Id));
        Assert.Equal(modded.Id, services.Sessions.RunningProfileId(rig.Game.Id));
        Assert.Equal([rig.Game.Id], services.Sessions.RunningGameIds());
        var rows = (await rig.Profiles.GetRowStatsAsync(rig.Game.Id, Ct)).Value!;
        Assert.True(rows.Single(row => row.ProfileId == modded.Id).IsRunning);
        Assert.False(rows.Single(row => row.ProfileId == vanilla.Id).IsRunning);
        Assert.NotNull((await rig.Profiles.ListAsync(rig.Game.Id, Ct)).Value!.Single(profile => profile.Id == modded.Id).LastLaunchedAt);
        Assert.Equal("error.gameService.gameRunning", (await rig.Workspace.Games.DeleteAsync(rig.Game.Id)).Error?.MessageId);
        Assert.Equal("error.gameService.gameRunning", (await rig.Profiles.DeleteAsync(rig.Game.Id, vanilla.Id)).Error?.MessageId);

        // A second launch is refused and starts no job; completing before the game has exited is refused and changes nothing.
        var launchJobs = rig.Jobs.List().Count(candidate => candidate.Kind == JobKind.Launch);
        var again = await rig.Launch.StartAsync(rig.Game.Id, Tip(modded));
        var early = await rig.Launch.CompleteSessionAsync(rig.Game.Id, new SessionCompletion.CaptureNew("Too early"));
        Assert.Equal("error.launch.alreadyRunning", again.Error?.MessageId);
        Assert.Equal(rig.Game.Id, again.Error!.Args["gameId"]);
        Assert.Equal(launchJobs, rig.Jobs.List().Count(candidate => candidate.Kind == JobKind.Launch));
        Assert.Equal("error.launch.sessionStillRunning", early.Error?.MessageId);
        Assert.IsType<SessionState.Running>(rig.Launch.GetSession(rig.Game.Id)!.State);

        // Played on: a new save, an old one changed, and the game's own files touched by a launcher.
        rig.WriteSave("slot3.sav", "a new save");
        rig.WriteSave("slot1.sav", "an old save, played on");
        rig.WriteInstall("mods/updated-mid-session.txt", "the launcher updated the game");

        // "I'm done playing" ends the session and leaves the game open; saying it twice is harmless.
        Assert.True((await rig.Launch.MarkFinishedAsync(rig.Game.Id)).Ok);
        var exited = Assert.IsType<SessionState.Exited>((await rig.WaitForAsync<SessionState.Exited>()).State);
        Assert.True((await rig.Launch.MarkFinishedAsync(rig.Game.Id)).Ok);
        Assert.True(exited.PlayDuration > TimeSpan.Zero);
        Assert.Equal(2, exited.NewFileCount);
        Assert.Equal(1, exited.ChangedInstallFileCount);
        Assert.NotEmpty(rig.GameProcessIds());

        // The session is over once the player has chosen, but the open game still blocks the next launch.
        Assert.True((await rig.Launch.CompleteSessionAsync(rig.Game.Id, new SessionCompletion.Skip())).Ok);
        var completed = Assert.IsType<SessionState.Completed>((await rig.WaitForAsync<SessionState.Completed>()).State);
        Assert.Equal(new SessionCompletion.Skip(), completed.Completion);
        Assert.Null(completed.Result);
        Assert.Null(completed.Error);
        Assert.False(services.Sessions.IsGameRunning(rig.Game.Id));
        Assert.Equal("error.launch.noActiveSession", (await rig.Launch.CompleteSessionAsync(rig.Game.Id, new SessionCompletion.Skip())).Error?.MessageId);
        Assert.Equal("error.launch.alreadyRunning", (await rig.Launch.StartAsync(rig.Game.Id, Tip(modded))).Error?.MessageId);

        // Closed and cleaned up, the game can be started again, and the new session replaces the old one.
        rig.KillGame();
        Assert.True(SpinWait.SpinUntil(() => rig.GameProcessIds().Count == 0, TimeSpan.FromSeconds(20)));
        File.Delete(Path.Combine(rig.SavesPath, "slot3.sav"));
        File.Delete(Path.Combine(rig.InstallPath, "mods", "updated-mid-session.txt"));
        rig.WriteSave("slot1.sav", "save one");
        File.Delete(rig.ReadyFile);
        var first = rig.Launch.GetSession(rig.Game.Id)!;
        var second = await rig.StartAsync(Tip(modded));
        await rig.WaitForGameAsync();

        var replaced = rig.Launch.GetSession(rig.Game.Id)!;
        Assert.Equal(second.JobId, replaced.LaunchJobId);
        Assert.IsType<SessionState.Running>(replaced.State);
        Assert.True(replaced.Revision > first.Revision);
        Assert.Single(rig.Launch.ListSessions());
    }

    [Fact(Timeout = 120_000)]
    public async Task A_game_that_is_open_outside_SavePoint_blocks_the_launch_by_its_process_name()
    {
        using var rig = await LaunchRig.CreateAsync(followProcessName: true);
        var vanilla = await rig.ProfileAsync("Vanilla");
        var outsideReady = Path.Combine(rig.Workspace.Root, "outside-ready.txt");
        using var outside = GameLaunchers.ForCurrentOs().StartExecutable(
            Path.Combine(rig.InstallPath, rig.Game.Launch.ExePath!),
            ["--ready-file", outsideReady],
            rig.InstallPath);
        await FakeGameProcess.WaitForReadyFileAsync(outsideReady);

        var started = await rig.Launch.StartAsync(rig.Game.Id, Tip(vanilla));

        Assert.Equal("error.launch.alreadyRunning", started.Error?.MessageId);
        Assert.Null(rig.Launch.GetSession(rig.Game.Id));
        Assert.DoesNotContain(rig.Jobs.List(), job => job.Kind == JobKind.Launch);

        // The refused call let go of the game: once the outside copy is closed, a launch goes through.
        Assert.False(rig.Workspace.Backend.Services.Sessions.IsGameRunning(rig.Game.Id));
        rig.KillGame();
        Assert.True(await FakeGameProcess.WaitUntilAsync(() => rig.GameProcessIds().Count == 0), "the outside copy never closed");
        Assert.Equal(JobStatus.Succeeded, (await rig.StartAsync(Tip(vanilla))).Status);
    }

    [Fact(Timeout = 120_000)]
    public async Task A_launch_whose_program_is_missing_fails_with_the_path_and_leaves_the_game_free()
    {
        using var rig = await LaunchRig.CreateAsync(exePath: "no-such-program.exe");
        var vanilla = await rig.ProfileAsync("Vanilla");

        var job = await rig.StartAsync(Tip(vanilla));
        var failed = await rig.WaitForAsync<SessionState.Failed>();

        Assert.Equal(JobStatus.Failed, job.Status);
        Assert.Equal("error.path.missing", job.Error?.MessageId);
        Assert.EndsWith("no-such-program.exe", (string)job.Error!.Args["path"]!, StringComparison.Ordinal);
        Assert.Equal(job.Error, Assert.IsType<SessionState.Failed>(failed.State).Error);
        Assert.False(rig.Workspace.Backend.Services.Sessions.IsGameRunning(rig.Game.Id));

        // The game lock was let go: a capture goes straight through.
        Assert.Equal(JobStatus.Succeeded, (await rig.CaptureAsync(vanilla, "Still works")).Status);
    }
}
