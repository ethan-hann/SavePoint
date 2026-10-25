using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Git;
using Savepoint.Core.Launch.Orchestration;

namespace Savepoint.Core.Tests.Backend;

// A game-wide rule for logs or crash dumps also reaches the saves folder, so it is never saved with a game whose saves it would hide,
// whatever profile holds them. Rules for the game's own folder only are never asked about, since they cannot reach the saves.
public class GamesLeaveOutTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static void Write(GameDefinition game, string relativePath) =>
        BackendWorkspace.WriteFile(Path.Combine(game.External!.Path, relativePath), "kept");

    private static async Task<AddGameOutcome.Added> AddAsync(BackendWorkspace workspace, GameDefinition game, IReadOnlyList<string>? supplied = null)
    {
        var added = await workspace.Games.AddAsync(game, supplied);
        Assert.True(added.Ok, added.Error?.Detail);
        return Assert.IsType<AddGameOutcome.Added>(added.Value);
    }

    [Fact]
    public async Task A_new_game_moves_the_game_wide_default_rules_that_would_hide_files_in_its_saves_folder_to_its_own_folder_only()
    {
        using var workspace = new BackendWorkspace();
        var game = workspace.NewGame() with { IgnorePaths = RunTimeOutput.DefaultRules };
        Write(game, "profile/player.log");

        var added = await AddAsync(workspace, game, RunTimeOutput.DefaultRules);

        Assert.Equal(["**/*.dmp", "**/*.mdmp"], added.Game.IgnorePaths);
        Assert.Equal(["**/*.log"], added.Game.InstallIgnorePaths);
        Assert.Equal(new RulesLeftOff(["**/*.log"], SavesHoldFiles: true), added.LeftOff);
        var stored = (await workspace.Games.ListAsync(Ct)).Value!.Single(listed => listed.Id == game.Id);
        Assert.Equal(["**/*.dmp", "**/*.mdmp"], stored.IgnorePaths);
        Assert.Equal(["**/*.log"], stored.InstallIgnorePaths);
        var preview = (await workspace.Games.GetCapturePreviewAsync(game.Id, Ct)).Value!.Folders.Single(folder => folder.Role == RepoRole.External);
        Assert.Equal(2, preview.FileCount);
    }

    [Fact]
    public async Task A_rule_moved_to_the_games_own_folder_still_keeps_its_logs_from_looking_like_an_update_there_and_keeps_the_save()
    {
        using var workspace = new BackendWorkspace();
        var game = workspace.NewGame() with { IgnorePaths = ["**/*.log", "SKSE/Plugins/*.log"] };
        Write(game, "Player.log");
        BackendWorkspace.WriteFile(Path.Combine(game.Install.Path, "crloader.log"), "loader");

        var added = await AddAsync(workspace, game, game.IgnorePaths);

        Assert.Equal(["SKSE/Plugins/*.log"], added.Game.IgnorePaths);
        Assert.Equal(["**/*.log"], added.Game.InstallIgnorePaths);
        var folders = (await workspace.Games.GetCapturePreviewAsync(game.Id, Ct)).Value!.Folders;
        Assert.Equal(2, folders.Single(folder => folder.Role == RepoRole.Install).FileCount);
        Assert.Equal(2, folders.Single(folder => folder.Role == RepoRole.External).FileCount);
    }

    [Fact]
    public async Task A_new_game_keeps_every_default_rule_when_its_saves_folder_holds_nothing_they_name()
    {
        using var workspace = new BackendWorkspace();
        var game = workspace.NewGame() with { IgnorePaths = [.. RunTimeOutput.DefaultRules, "Saves/*.tmp"] };

        var added = await AddAsync(workspace, game);

        Assert.Equal(["**/*.log", "**/*.dmp", "**/*.mdmp", "Saves/*.tmp"], added.Game.IgnorePaths);
        Assert.Null(added.LeftOff);
    }

    [Fact]
    public async Task A_rule_the_player_wrote_that_is_not_a_default_is_kept_even_if_it_matches_a_save()
    {
        using var workspace = new BackendWorkspace();
        var game = workspace.NewGame() with { IgnorePaths = ["**/*.sav.bak"] };
        Write(game, "slot1.sav.bak");

        var added = await AddAsync(workspace, game);

        Assert.Equal(["**/*.sav.bak"], added.Game.IgnorePaths);
        Assert.Null(added.LeftOff);
    }

    [Fact]
    public async Task A_default_looking_rule_the_player_typed_stays_game_wide_even_when_it_names_a_save_file()
    {
        using var workspace = new BackendWorkspace();
        var game = workspace.NewGame() with { IgnorePaths = ["**/*.log"] };
        Write(game, "profile/player.log");

        var added = await AddAsync(workspace, game);

        Assert.Equal(["**/*.log"], added.Game.IgnorePaths);
        Assert.Empty(added.Game.InstallIgnorePaths);
        Assert.Null(added.LeftOff);
        var stored = (await workspace.Games.ListAsync(Ct)).Value!.Single(listed => listed.Id == game.Id);
        Assert.Equal(["**/*.log"], stored.IgnorePaths);
    }

    [Fact]
    public async Task Only_the_rules_SavePoint_supplied_move_when_a_typed_one_has_the_same_text_as_another_default()
    {
        using var workspace = new BackendWorkspace();
        var game = workspace.NewGame() with { IgnorePaths = ["**/*.log", "**/*.dmp"] };
        Write(game, "profile/player.log");
        Write(game, "profile/player.dmp");

        var added = await AddAsync(workspace, game, ["**/*.log"]);

        Assert.Equal(["**/*.dmp"], added.Game.IgnorePaths);
        Assert.Equal(["**/*.log"], added.Game.InstallIgnorePaths);
        Assert.Equal(new RulesLeftOff(["**/*.log"], SavesHoldFiles: true), added.LeftOff);
    }

    [Fact]
    public async Task A_typed_default_looking_rule_is_kept_when_the_saves_folder_cannot_be_checked()
    {
        using var workspace = new BackendWorkspace(options => options with
        {
            Engine = new GitEngineOptions
            {
                InvocationObserver = invocation =>
                {
                    if (invocation.Arguments.Contains("-o") && invocation.Arguments.Contains("-i"))
                    {
                        throw new IOException("the drive went away");
                    }
                },
            },
        });
        var game = workspace.NewGame() with { IgnorePaths = ["**/*.log"] };

        var added = await AddAsync(workspace, game);

        Assert.Equal(["**/*.log"], added.Game.IgnorePaths);
        Assert.Null(added.LeftOff);
    }

    [Fact]
    public async Task A_game_without_a_saves_folder_keeps_all_its_default_rules()
    {
        using var workspace = new BackendWorkspace();
        var game = workspace.NewGame(withSaves: false) with { IgnorePaths = RunTimeOutput.DefaultRules };

        var added = await AddAsync(workspace, game);

        Assert.Equal(RunTimeOutput.DefaultRules, added.Game.IgnorePaths);
        Assert.Null(added.LeftOff);
    }

    [Fact]
    public async Task Rules_for_the_games_own_folder_are_kept_and_the_saves_folder_is_not_even_looked_at()
    {
        var looked = 0;
        using var workspace = new BackendWorkspace(options => options with { Engine = new GitEngineOptions { InvocationObserver = invocation => CountWalks(invocation, ref looked) } });
        var game = workspace.NewGame() with { InstallIgnorePaths = RunTimeOutput.DefaultRules };
        Write(game, "profile/player.log");
        Interlocked.Exchange(ref looked, 0);

        var added = await AddAsync(workspace, game);

        Assert.Equal(RunTimeOutput.DefaultRules, added.Game.InstallIgnorePaths);
        Assert.Empty(added.Game.IgnorePaths);
        Assert.Null(added.LeftOff);
        Assert.Equal(0, Volatile.Read(ref looked));
    }

    private static void CountWalks(GitInvocation invocation, ref int count)
    {
        if (invocation.Arguments.Contains("-o") && invocation.Arguments.Contains("-i"))
        {
            Interlocked.Increment(ref count);
        }
    }

    [Fact]
    public async Task A_file_only_another_profile_recorded_still_keeps_the_rule_off_after_the_history_is_reconnected()
    {
        using var workspace = new BackendWorkspace(options => options with { Launch = LaunchOrchestrator.Factory(new LaunchOrchestratorOptions()) });
        var game = await workspace.AddGameAsync();
        var (first, _) = await workspace.CreateProfileAsync(game, "A");
        Write(game, "journal.log");
        await workspace.CreateProfileAsync(game, "B");

        // Back on profile A, whose point has no journal.log: only profile B's newest point holds it.
        var switched = await workspace.Backend.Launch.SwitchAsync(game.Id, new LaunchTarget.ProfileTip(first.Id));
        Assert.True(switched.Ok, switched.Error?.Detail);
        Assert.Equal(JobStatus.Succeeded, (await workspace.WaitForJobAsync(switched.Value.JobId)).Status);
        Assert.False(File.Exists(Path.Combine(game.External!.Path, "journal.log")));
        File.Delete(Path.Combine(workspace.CatalogDirectory, CatalogStore.GamesFileName));
        File.Delete(Path.Combine(workspace.CatalogDirectory, CatalogStore.ProfilesFileName));
        workspace.Restart();

        var resolved = await workspace.Games.ResolveOrphanAsync(
            game with { Id = "alpha-again", IgnorePaths = RunTimeOutput.DefaultRules },
            new OrphanResolution.Reconnect(game.Id, [new RecoveredProfile(first.BranchName, "A")]),
            RunTimeOutput.DefaultRules);

        Assert.True(resolved.Ok, resolved.Error?.Detail);
        Assert.Equal(["**/*.dmp", "**/*.mdmp"], resolved.Value.Game.IgnorePaths);
        Assert.Equal(["**/*.log"], resolved.Value.Game.InstallIgnorePaths);
        Assert.Equal(new RulesLeftOff(["**/*.log"], SavesHoldFiles: true), resolved.Value.LeftOff);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task When_the_saves_folder_cannot_be_checked_the_game_is_still_added_with_the_rules_for_its_own_folder_only_and_says_it_could_not_check(bool unauthorized)
    {
        using var workspace = new BackendWorkspace(options => options with
        {
            Engine = new GitEngineOptions
            {
                InvocationObserver = invocation =>
                {
                    if (invocation.Arguments.Contains("-o") && invocation.Arguments.Contains("-i"))
                    {
                        throw unauthorized ? new UnauthorizedAccessException("denied") : new IOException("the drive went away");
                    }
                },
            },
        });
        var game = workspace.NewGame() with { IgnorePaths = [.. RunTimeOutput.DefaultRules, "Saves/*.tmp"] };

        var added = await AddAsync(workspace, game, game.IgnorePaths);

        Assert.Equal(["Saves/*.tmp"], added.Game.IgnorePaths);
        Assert.Equal(RunTimeOutput.DefaultRules, added.Game.InstallIgnorePaths);
        Assert.Equal(new RulesLeftOff(RunTimeOutput.DefaultRules, SavesHoldFiles: false), added.LeftOff);
        var stored = (await workspace.Games.ListAsync(Ct)).Value!.Single();
        Assert.Equal(["Saves/*.tmp"], stored.IgnorePaths);
        Assert.Equal(RunTimeOutput.DefaultRules, stored.InstallIgnorePaths);
    }
}
