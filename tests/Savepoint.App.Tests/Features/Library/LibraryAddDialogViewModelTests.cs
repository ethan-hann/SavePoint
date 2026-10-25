using Savepoint.App.Features.Library;
using Savepoint.App.Tests.Fakes;
using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;

namespace Savepoint.App.Tests.Features.Library;

public class LibraryAddDialogViewModelTests
{
    private static (LibraryAddDialogViewModel Dialog, FakeBackend Backend) Make(params LibraryGame[] games)
    {
        var backend = FakeBackend.Empty();
        backend.State.Library = LibraryTestData.Scan(games);
        return (new LibraryAddDialogViewModel(backend.Games, games, LibraryTestData.Text), backend);
    }

    private static IReadOnlyList<string> AddedKeys(FakeBackend backend) =>
        [.. backend.Calls.All.Where(call => call.Member == nameof(IGamesApi.AddLibraryGameAsync)).Select(call => ((LibraryGame)call.Args[0]!).Key)];

    [Fact]
    public async Task Every_game_is_added_in_the_order_given_and_the_summary_counts_them()
    {
        var (dialog, backend) = Make(LibraryTestData.Game("A", id: "1"), LibraryTestData.Game("B", id: "2"), LibraryTestData.Game("C", id: "3"));

        await dialog.RunAsync();

        Assert.Equal(["steam:1", "steam:2", "steam:3"], AddedKeys(backend));
        Assert.True(dialog.IsFinished);
        Assert.Equal("Finished adding games", dialog.Heading);
        Assert.Equal("3 games were added.", dialog.AddedSummary);
        Assert.Equal(3, (await Summary(dialog)).Added.Count);
        Assert.Equal(["A", "B", "C"], (await backend.Games.ListAsync(TestContext.Current.CancellationToken)).Value!.Select(g => g.Name));
    }

    [Fact]
    public async Task Progress_names_the_game_being_added_and_how_far_along_the_list_is()
    {
        var (dialog, _) = Make(LibraryTestData.Game("A", id: "1"), LibraryTestData.Game("B", id: "2"));
        var seen = new List<string>();
        dialog.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(dialog.ProgressText))
            {
                seen.Add(dialog.ProgressText);
            }
        };

        await dialog.RunAsync();

        Assert.Contains("Adding 1 of 2: A", seen);
        Assert.Contains("Adding 2 of 2: B", seen);
        Assert.Equal(100, dialog.PercentValue);
    }

    [Fact]
    public async Task A_refused_game_is_reported_with_its_reason_and_the_next_game_is_still_added()
    {
        var bad = LibraryTestData.Game("Steam Library", id: "1");
        var good = LibraryTestData.Game("Good", id: "2");
        var (dialog, backend) = Make(bad, good);
        backend.State.LibraryRefusals[bad.Key] = ErrorInfo.From(new PathSafetyException(@"C:\Games\Steam Library", 2));

        await dialog.RunAsync();

        Assert.Equal(["steam:1", "steam:2"], AddedKeys(backend));
        Assert.Equal("1 game was added.", dialog.AddedSummary);
        Assert.Equal("1 game could not be added.", dialog.NotAddedSummary);
        var line = Assert.Single(dialog.ResultLines, l => l.Kind == LibraryResultKind.NotAdded);
        Assert.StartsWith("Steam Library: not added.", line.Text);
        Assert.EndsWith(LibraryTestData.Text.Message(backend.State.LibraryRefusals[bad.Key]), line.Text, StringComparison.Ordinal);
        Assert.NotEqual("Steam Library: not added. ", line.Text);
    }

    [Fact]
    public async Task A_game_that_was_already_added_is_counted_and_not_added_again()
    {
        var (dialog, backend) = Make(LibraryTestData.Game("A", id: "1"));
        await backend.Games.AddAsync(LibraryTestData.Game("A", id: "1").Draft);

        await dialog.RunAsync();

        Assert.Equal("1 game was already added.", dialog.AlreadyAddedSummary);
        Assert.Equal("No games were added.", dialog.AddedSummary);
        Assert.Single((await backend.Games.ListAsync(TestContext.Current.CancellationToken)).Value!);
    }

    [Fact]
    public async Task A_game_with_older_snapshots_is_reported_with_what_to_do_about_it()
    {
        var game = LibraryTestData.Game("Alpha", id: "1");
        var (dialog, backend) = Make(game);
        backend.State.Orphans[game.Draft.Install.Path] = new OrphanedHistory("alpha-0", [RepoRole.Install], []);

        await dialog.RunAsync();

        var line = Assert.Single(dialog.ResultLines, l => l.Kind == LibraryResultKind.NotAdded);
        Assert.Contains("snapshots from a game you removed earlier", line.Text);
        Assert.Contains("Add a game", line.Text);
        Assert.Equal("1 game could not be added.", dialog.NotAddedSummary);
    }

    [Fact]
    public async Task A_saves_folder_that_was_left_out_is_named_so_the_player_can_add_it_later()
    {
        var (dialog, _) = Make(LibraryTestData.Game("Skyrim", id: "1", saves: @"C:\Docs\Skyrim\Saves", savesExist: false));

        await dialog.RunAsync();

        var line = Assert.Single(dialog.ResultLines, l => l.Kind == LibraryResultKind.Notice);
        Assert.StartsWith("Skyrim: added without a saves folder.", line.Text);
        Assert.Contains(@"C:\Docs\Skyrim\Saves", line.Text);
        Assert.Equal("1 game was added.", dialog.AddedSummary);
    }

    [Theory]
    [InlineData(false, "Skyrim: the rule **/*.log now applies to the game's own folder only, because its saves folder holds files it would have left out. SavePoint keeps your saves in your snapshots.")]
    [InlineData(true, "Skyrim: the rule **/*.log now applies to the game's own folder only, because SavePoint could not look in its saves folder to be sure it would keep your saves. You can see it under \"Files to leave out of the game's own folder\" in Edit game.")]
    public async Task A_rule_that_now_applies_to_the_games_own_folder_only_is_said_in_a_notice(bool checkFails, string expected)
    {
        var game = LibraryTestData.Game("Skyrim", id: "1");
        game = game with { Draft = game.Draft with { IgnorePaths = ["**/*.log"] } };
        var (dialog, backend) = Make(game);
        backend.Games.SavesLeftOutByRule["**/*.log"] = ["Player.log"];
        backend.Games.SavesCheckFails = checkFails;

        await dialog.RunAsync();

        Assert.Equal(expected, Assert.Single(dialog.ResultLines, l => l.Kind == LibraryResultKind.Notice).Text);
        var stored = Assert.Single((await backend.Games.ListAsync(TestContext.Current.CancellationToken)).Value!);
        Assert.Equal(["**/*.log"], stored.InstallIgnorePaths);
        Assert.Empty(stored.IgnorePaths);
    }

    [Fact]
    public async Task Stopping_after_a_game_leaves_the_rest_untried_and_says_how_many()
    {
        var (dialog, backend) = Make(LibraryTestData.Game("A", id: "1"), LibraryTestData.Game("B", id: "2"), LibraryTestData.Game("C", id: "3"));
        dialog.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(dialog.ProgressText) && dialog.ProgressText.StartsWith("Adding 1 of", StringComparison.Ordinal))
            {
                dialog.StopCommand.Execute(null);
            }
        };

        await dialog.RunAsync();

        Assert.Equal(["steam:1"], AddedKeys(backend));
        Assert.Equal(2, (await Summary(dialog)).NotTried);
        Assert.Contains(dialog.ResultLines, l => l.Text == "2 games were not tried because you stopped.");
    }

    [Fact]
    public async Task The_dialog_cannot_be_dismissed_until_the_run_has_finished()
    {
        var (dialog, _) = Make(LibraryTestData.Game("A", id: "1"));
        var whileRunning = dialog.CanDismiss;

        await dialog.RunAsync();

        Assert.False(whileRunning);
        Assert.True(dialog.CanDismiss);
        Assert.Equal("Finished adding games", dialog.Heading);
    }

    [Fact]
    public async Task Done_answers_with_the_summary_of_the_run()
    {
        var (dialog, _) = Make(LibraryTestData.Game("A", id: "1"));
        await dialog.RunAsync();

        dialog.CloseCommand.Execute(null);

        var summary = await dialog.Result;
        Assert.Equal(["steam:1"], summary.Results.Select(r => r.Key));
    }

    [Fact]
    public async Task A_failed_call_counts_as_a_game_that_could_not_be_added_and_the_run_goes_on()
    {
        var (dialog, backend) = Make(LibraryTestData.Game("A", id: "1"), LibraryTestData.Game("B", id: "2"));
        backend.FailNext<IGamesApi>(nameof(IGamesApi.AddLibraryGameAsync), ErrorInfo.From(new CatalogCorruptException("games.json", null)));

        await dialog.RunAsync();

        Assert.Equal("1 game could not be added.", dialog.NotAddedSummary);
        Assert.Equal("1 game was added.", dialog.AddedSummary);
    }

    [Fact]
    public async Task Running_twice_adds_nothing_the_second_time()
    {
        var (dialog, backend) = Make(LibraryTestData.Game("A", id: "1"));

        await dialog.RunAsync();
        await dialog.RunAsync();

        Assert.Single(AddedKeys(backend));
    }

    [Fact]
    public async Task A_game_whose_add_throws_ends_the_run_cleanly_and_can_still_be_closed()
    {
        var games = new[] { LibraryTestData.Game("A", id: "1"), LibraryTestData.Game("B", id: "2"), LibraryTestData.Game("C", id: "3") };
        var calls = 0;
        var dialog = new LibraryAddDialogViewModel(
            game =>
            {
                calls++;
                return calls == 2
                    ? throw new InvalidOperationException("boom")
                    : Task.FromResult(OperationResult.Success(new LibraryAddResult(game.Key, game.Name, LibraryAddStatus.Added, game.Draft.Id)));
            },
            games,
            LibraryTestData.Text);

        await dialog.RunAsync();

        Assert.True(dialog.IsFinished);
        Assert.True(dialog.CanDismiss);
        Assert.Equal("1 game was added.", dialog.AddedSummary);
        Assert.Equal("1 game could not be added.", dialog.NotAddedSummary);
        dialog.CloseCommand.Execute(null);
        var summary = await dialog.Result;
        Assert.Equal(["steam:1", "steam:2"], summary.Results.Select(r => r.Key));
        Assert.Equal(1, summary.NotTried);
    }

    private static async Task<LibraryAddSummary> Summary(LibraryAddDialogViewModel dialog)
    {
        dialog.CloseCommand.Execute(null);
        return await dialog.Result;
    }
}
