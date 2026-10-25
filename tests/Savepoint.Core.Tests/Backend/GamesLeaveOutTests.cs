using Savepoint.Core.Api;
using Savepoint.Core.Catalog;

namespace Savepoint.Core.Tests.Backend;

// A rule that leaves out logs or crash dumps must never leave out a file of the saves folder, whether the rule is a new game's default
// or one offered after play.
public class GamesLeaveOutTests
{
    private static void Write(GameDefinition game, string relativePath) =>
        BackendWorkspace.WriteFile(Path.Combine(game.External!.Path, relativePath), "kept");

    [Fact]
    public async Task A_new_game_drops_the_default_rules_that_would_leave_out_files_in_its_saves_folder()
    {
        using var workspace = new BackendWorkspace();
        var game = workspace.NewGame() with { IgnorePaths = RunTimeOutput.DefaultRules };
        Write(game, "profile/player.log");

        var added = await workspace.AddGameAsync(game);

        Assert.Equal(["**/*.dmp", "**/*.mdmp"], added.IgnorePaths);
        var stored = (await workspace.Games.ListAsync(workspace.Token)).Value!.Single(listed => listed.Id == game.Id);
        Assert.Equal(["**/*.dmp", "**/*.mdmp"], stored.IgnorePaths);
        var preview = (await workspace.Games.GetCapturePreviewAsync(game.Id, workspace.Token)).Value!.Folders.Single(folder => folder.Role == RepoRole.External);
        Assert.Equal(2, preview.FileCount);
    }

    [Fact]
    public async Task A_new_game_keeps_every_default_rule_when_its_saves_folder_holds_nothing_they_name()
    {
        using var workspace = new BackendWorkspace();
        var game = workspace.NewGame() with { IgnorePaths = [.. RunTimeOutput.DefaultRules, "Saves/*.tmp"] };

        var added = await workspace.AddGameAsync(game);

        Assert.Equal(["**/*.log", "**/*.dmp", "**/*.mdmp", "Saves/*.tmp"], added.IgnorePaths);
    }

    [Fact]
    public async Task A_rule_the_player_wrote_that_is_not_a_default_is_kept_even_if_it_matches_a_save()
    {
        using var workspace = new BackendWorkspace();
        var game = workspace.NewGame() with { IgnorePaths = ["**/*.sav.bak"] };
        Write(game, "slot1.sav.bak");

        var added = await workspace.AddGameAsync(game);

        Assert.Equal(["**/*.sav.bak"], added.IgnorePaths);
    }

    [Fact]
    public async Task A_game_without_a_saves_folder_keeps_all_its_default_rules()
    {
        using var workspace = new BackendWorkspace();
        var game = workspace.NewGame(withSaves: false) with { IgnorePaths = RunTimeOutput.DefaultRules };

        var added = await workspace.AddGameAsync(game);

        Assert.Equal(RunTimeOutput.DefaultRules, added.IgnorePaths);
    }

    [Fact]
    public async Task Asking_about_rules_names_the_safe_ones_and_the_files_the_others_would_leave_out()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync();
        Write(game, "crash.dmp");
        Write(game, "profile/player.log");

        var check = (await workspace.Games.CheckLeaveOutAsync(game.Id, RunTimeOutput.DefaultRules, workspace.Token)).Value!;

        Assert.Equal(["**/*.mdmp"], check.Safe);
        Assert.Equal(["**/*.log", "**/*.dmp"], check.Blocked.Select(blocked => blocked.Rule));
        Assert.Equal(["profile/player.log"], check.Blocked[0].Files);
        Assert.Equal(["crash.dmp"], check.Blocked[1].Files);
    }

    [Fact]
    public async Task A_save_the_game_already_recorded_blocks_a_rule_too()
    {
        using var workspace = new BackendWorkspace();
        var game = workspace.NewGame();
        Write(game, "journal.log");
        game = await workspace.AddGameAsync(game);
        await workspace.CreateProfileAsync(game, "Vanilla");

        var check = (await workspace.Games.CheckLeaveOutAsync(game.Id, ["**/*.log"], workspace.Token)).Value!;

        Assert.Empty(check.Safe);
        Assert.Equal(["journal.log"], Assert.Single(check.Blocked).Files);
    }

    [Fact]
    public async Task Only_a_few_names_are_given_for_a_rule_that_matches_many_files()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync();
        for (var i = 0; i < 9; i++)
        {
            Write(game, $"logs/{i}.log");
        }

        var check = (await workspace.Games.CheckLeaveOutAsync(game.Id, ["**/*.log"], workspace.Token)).Value!;

        Assert.Equal(RuleBlocked.Sample, Assert.Single(check.Blocked).Files.Count);
    }

    [Fact]
    public async Task A_game_with_no_saves_folder_or_a_missing_one_blocks_nothing()
    {
        using var workspace = new BackendWorkspace();
        var bare = await workspace.AddGameAsync(workspace.NewGame(withSaves: false));
        var gone = await workspace.AddGameAsync(workspace.NewGame("Beta"));
        Directory.Delete(gone.External!.Path, recursive: true);

        Assert.Equal(["**/*.log"], (await workspace.Games.CheckLeaveOutAsync(bare.Id, ["**/*.log"], workspace.Token)).Value!.Safe);
        Assert.Equal(["**/*.log"], (await workspace.Games.CheckLeaveOutAsync(gone.Id, ["**/*.log"], workspace.Token)).Value!.Safe);
    }

    [Fact]
    public async Task Asking_about_an_unknown_game_fails_and_asking_changes_nothing()
    {
        using var workspace = new BackendWorkspace();
        var game = await workspace.AddGameAsync();
        Write(game, "a.log");

        var unknown = await workspace.Games.CheckLeaveOutAsync("nope", ["**/*.log"], workspace.Token);
        await workspace.Games.CheckLeaveOutAsync(game.Id, ["**/*.log"], workspace.Token);

        Assert.Equal("error.game.notFound", unknown.Error?.MessageId);
        Assert.Empty((await workspace.Games.ListAsync(workspace.Token)).Value!.Single().IgnorePaths);
        var preview = (await workspace.Games.GetCapturePreviewAsync(game.Id, workspace.Token)).Value!.Folders.Single(folder => folder.Role == RepoRole.External);
        Assert.Equal(2, preview.FileCount);
    }
}
