using Savepoint.Core.Api;
using Savepoint.Core.Catalog;

namespace Savepoint.Core.Tests.Launch.Orchestration;

// After play, the files that changed in the game's own folder are told apart: logs and crash dumps the game or its mod loader rewrites
// are not an update, and the session says so.
public class LaunchRunTimeOutputTests
{
    private static async Task<SessionState.Exited> PlayAsync(LaunchRig rig, Profile profile, Action<LaunchRig> play)
    {
        await rig.StartAsync(new LaunchTarget.ProfileTip(profile.Id));
        await rig.WaitForGameAsync();
        play(rig);
        Assert.True((await rig.Launch.MarkFinishedAsync(rig.Game.Id)).Ok);
        return Assert.IsType<SessionState.Exited>((await rig.WaitForAsync<SessionState.Exited>()).State);
    }

    [Fact(Timeout = 180_000)]
    public async Task A_session_where_only_logs_changed_in_the_game_folder_names_the_rule_that_covers_them()
    {
        var (rig, _, modded) = await LaunchRig.WithTwoProfilesAsync();
        using var _ = rig;

        var exited = await PlayAsync(rig, modded, game =>
        {
            game.WriteInstall("crloader.log", "loader started");
            game.WriteInstall("mods/a/a.log", "mod a started");
        });

        Assert.Equal(2, exited.ChangedInstallFileCount);
        Assert.Equal(["**/*.log"], exited.LeaveOutRules);
    }

    [Fact(Timeout = 180_000)]
    public async Task A_log_beside_a_changed_game_or_mod_file_is_still_an_update_and_offers_no_rule()
    {
        var (rig, _, modded) = await LaunchRig.WithTwoProfilesAsync();
        using var _ = rig;

        var exited = await PlayAsync(rig, modded, game =>
        {
            game.WriteInstall("crloader.log", "loader started");
            game.WriteInstall("mods/updated.txt", "the launcher updated a mod");
        });

        Assert.Equal(2, exited.ChangedInstallFileCount);
        Assert.Null(exited.LeaveOutRules);
    }

    [Fact(Timeout = 180_000)]
    public async Task A_session_that_changed_nothing_in_the_game_folder_offers_no_rule()
    {
        var (rig, _, modded) = await LaunchRig.WithTwoProfilesAsync();
        using var _ = rig;

        var exited = await PlayAsync(rig, modded, game => game.WriteSave("slot9.sav", "a save"));

        Assert.Equal(0, exited.ChangedInstallFileCount);
        Assert.Null(exited.LeaveOutRules);
    }

    [Fact(Timeout = 180_000)]
    public async Task Adding_the_rule_during_the_dialog_keeps_the_logs_out_of_the_save_that_follows()
    {
        var (rig, _, modded) = await LaunchRig.WithTwoProfilesAsync();
        using var _ = rig;
        var exited = await PlayAsync(rig, modded, game => game.WriteInstall("crloader.log", "loader started"));

        await rig.EditGameAsync(game => game with { IgnorePaths = RunTimeOutput.Merge(game.IgnorePaths, exited.LeaveOutRules!) });

        var status = await rig.StatusAsync(RepoRole.Install);
        Assert.Empty(status.UntrackedPaths);
        Assert.Empty(status.ChangedPaths);
        Assert.True((await rig.Launch.CompleteSessionAsync(rig.Game.Id, new SessionCompletion.CaptureNew("After play"))).Ok);
        Assert.IsType<SessionState.Completed>((await rig.WaitForAsync<SessionState.Completed>()).State);
    }
}
