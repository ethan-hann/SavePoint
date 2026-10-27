using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Git;

namespace Savepoint.Core.Tests.Launch.Orchestration;

// After play, the files that changed in the game's own folder are told apart: logs and crash dumps the game or its mod loader rewrites
// are not an update, and the session says so.
public class LaunchRunTimeOutputTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static async Task<SessionState.Exited> PlayAsync(LaunchRig rig, Profile profile, Action<LaunchRig> play)
    {
        await rig.StartAsync(new LaunchTarget.ProfileTip(profile.Id));
        await rig.WaitForGameAsync();
        play(rig);
        rig.KillGame();
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
        Assert.Equal(["**/*.log"], exited.LeaveOutRules);

        await rig.EditGameAsync(game => game with { InstallIgnorePaths = RunTimeOutput.Merge(game.InstallIgnorePaths, exited.LeaveOutRules!) });

        var status = await rig.StatusAsync(RepoRole.Install);
        Assert.Empty(status.UntrackedPaths);
        Assert.Empty(status.ChangedPaths);
        Assert.True((await rig.Launch.CompleteSessionAsync(rig.Game.Id, new SessionCompletion.CaptureNew("After play"))).Ok);
        Assert.IsType<SessionState.Completed>((await rig.WaitForAsync<SessionState.Completed>()).State);
        var newest = (await rig.CommitsAsync(RepoRole.Install, modded))[0];
        var files = await rig.WithEngineAsync((engine, repo) => engine.ListTreeFilesAsync(repo, newest.Id, Ct), RepoRole.Install);
        Assert.DoesNotContain("crloader.log", files);
        Assert.Contains("mods/a.txt", files);
    }

    [Fact(Timeout = 180_000)]
    public async Task A_log_that_earlier_snapshots_recorded_is_dropped_from_the_next_one_and_kept_in_the_earlier_ones()
    {
        using var rig = await LaunchRig.CreateAsync();
        rig.WriteInstall("crloader.log", "loader started");
        var vanilla = await rig.ProfileAsync("Vanilla");
        var before = (await rig.CommitsAsync(RepoRole.Install, vanilla))[0];
        var exited = await PlayAsync(rig, vanilla, game => game.WriteInstall("crloader.log", "loader started again"));
        Assert.Equal(1, exited.ChangedInstallFileCount);
        Assert.Equal(["**/*.log"], exited.LeaveOutRules);

        await rig.EditGameAsync(game => game with { InstallIgnorePaths = RunTimeOutput.Merge(game.InstallIgnorePaths, exited.LeaveOutRules!) });

        var status = await rig.StatusAsync(RepoRole.Install);
        Assert.Empty(status.ChangedPaths);
        Assert.Empty(status.UntrackedPaths);

        // A change to a real game file, made after the rule, is what gives the next snapshot something to record.
        rig.WriteInstall("data/patch.pak", "a real update");
        Assert.True((await rig.Launch.CompleteSessionAsync(rig.Game.Id, new SessionCompletion.CaptureNew("After play"))).Ok);
        Assert.IsType<SessionState.Completed>((await rig.WaitForAsync<SessionState.Completed>()).State);
        var commits = await rig.CommitsAsync(RepoRole.Install, vanilla);
        Assert.NotEqual(before.Id, commits[0].Id);
        var newest = await rig.WithEngineAsync((engine, repo) => engine.ListTreeFilesAsync(repo, commits[0].Id, Ct), RepoRole.Install);
        var earlier = await rig.WithEngineAsync((engine, repo) => engine.ListTreeFilesAsync(repo, before.Id, Ct), RepoRole.Install);
        Assert.DoesNotContain("crloader.log", newest);
        Assert.Contains("data/patch.pak", newest);
        Assert.Contains("crloader.log", earlier);
        Assert.Equal("loader started again", rig.ReadInstall("crloader.log"));
        Assert.Empty((await rig.StatusAsync(RepoRole.Install)).ChangedPaths);
    }

    [Theory(Timeout = 180_000)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task When_git_cannot_be_asked_about_the_logs_the_exit_summary_still_counts_the_files_and_offers_no_rule(bool unauthorized)
    {
        var armed = 0;
        using var rig = await LaunchRig.CreateAsync(configure: options => options with
        {
            Engine = new GitEngineOptions
            {
                InvocationObserver = invocation =>
                {
                    if (Volatile.Read(ref armed) == 1 && invocation.Arguments.Contains("--index-info"))
                    {
                        throw unauthorized ? new UnauthorizedAccessException("denied") : new IOException("the drive went away");
                    }
                },
            },
        });
        var vanilla = await rig.ProfileAsync("Vanilla");
        await rig.StartAsync(new LaunchTarget.ProfileTip(vanilla.Id));
        await rig.WaitForGameAsync();
        rig.WriteInstall("crloader.log", "loader started");
        rig.WriteSave("slot9.sav", "a save");
        Volatile.Write(ref armed, 1);
        Assert.True((await rig.Launch.MarkFinishedAsync(rig.Game.Id)).Ok);

        var exited = Assert.IsType<SessionState.Exited>((await rig.WaitForAsync<SessionState.Exited>()).State);

        Assert.Equal(1, exited.ChangedInstallFileCount);
        Assert.True(exited.NewFileCount >= 1);
        Assert.Null(exited.LeaveOutRules);
    }

    [Fact(Timeout = 180_000)]
    public async Task The_offered_rule_follows_the_folders_own_letter_case_setting()
    {
        var (rig, _, modded) = await LaunchRig.WithTwoProfilesAsync();
        using var _ = rig;

        var exited = await PlayAsync(rig, modded, game => game.WriteInstall("LOUD.LOG", "shouting"));

        // git matches by the folder's own core.ignorecase, which is what the folder was given when it was made.
        var config = await rig.WithEngineAsync((_, repo) => Task.FromResult(File.ReadAllText(Path.Combine(repo.GitDir, "config"))), RepoRole.Install);
        var ignoresCase = System.Text.RegularExpressions.Regex.IsMatch(config, @"ignorecase\s*=\s*true", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        IReadOnlyList<string>? expected = ignoresCase ? ["**/*.log"] : null;
        Assert.Equal(expected, exited.LeaveOutRules);
    }
}
