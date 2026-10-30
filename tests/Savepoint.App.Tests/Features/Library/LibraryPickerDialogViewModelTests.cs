using Savepoint.App.Features.Library;
using Savepoint.App.Tests.Fakes;
using Savepoint.Core.Api;
using Savepoint.Core.Errors;

namespace Savepoint.App.Tests.Features.Library;

public class LibraryPickerDialogViewModelTests
{
    private static async Task<(LibraryPickerDialogViewModel Picker, FakeBackend Backend)> LoadedAsync(LibraryScan? scan, Action<FakeBackend>? script = null)
    {
        var backend = FakeBackend.Empty();
        backend.State.Library = scan;
        script?.Invoke(backend);
        var picker = new LibraryPickerDialogViewModel(backend.Games, LibraryTestData.Text);
        await picker.LoadAsync();
        return (picker, backend);
    }

    private static IReadOnlyList<string> Visible(LibraryPickerDialogViewModel picker) => [.. picker.VisibleRows.Select(row => row.Name)];

    // Anything watching IsLoading acts on the state it finds when the flag drops.
    [Fact]
    public async Task Loading_ends_only_once_the_list_holds_the_games_found()
    {
        var backend = FakeBackend.Empty();
        backend.State.Library = LibraryTestData.Scan(LibraryTestData.Game("Alpha", id: "1"), LibraryTestData.Game("Beta", id: "2"));
        var picker = new LibraryPickerDialogViewModel(backend.Games, LibraryTestData.Text);
        (int Shown, bool ShowList)? whenLoadingEnded = null;
        picker.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(LibraryPickerDialogViewModel.IsLoading) && !picker.IsLoading)
            {
                whenLoadingEnded ??= (picker.VisibleRows.Count, picker.ShowList);
            }
        };

        await picker.LoadAsync();

        Assert.Equal((2, true), whenLoadingEnded);
    }

    [Fact]
    public async Task The_pointer_to_the_game_list_closes_the_checklist_without_choosing_games()
    {
        var (picker, _) = await LoadedAsync(LibraryTestData.Scan(LibraryTestData.Game("Alpha", id: "1")));
        picker.SelectAllCommand.Execute(null);
        Assert.False(picker.TemplateListRequested);

        picker.OpenTemplateListCommand.Execute(null);

        Assert.True(picker.TemplateListRequested);
        Assert.Null(await picker.Result);
    }

    [Fact]
    public async Task The_pointer_names_the_stores_the_scan_cannot_see()
    {
        var (picker, _) = await LoadedAsync(LibraryTestData.Scan());

        Assert.Contains("Epic", picker.TemplatePointerText);
        Assert.Contains("Game Pass", picker.TemplatePointerText);
    }

    [Fact]
    public async Task Backing_out_does_not_ask_for_the_game_list()
    {
        var (picker, _) = await LoadedAsync(LibraryTestData.Scan());

        picker.CancelCommand.Execute(null);

        Assert.False(picker.TemplateListRequested);
    }

    [Fact]
    public async Task Every_game_found_is_listed_with_its_store_folder_and_saves_line()
    {
        var skyrim = LibraryTestData.Game("Skyrim", saves: @"C:\Users\you\Documents\My Games\Skyrim\Saves");
        var deus = LibraryTestData.Game("Deus Ex", LibrarySource.Gog, "77");
        var (picker, _) = await LoadedAsync(LibraryTestData.Scan(skyrim, deus));

        var rows = picker.VisibleRows.ToDictionary(row => row.Name);

        Assert.False(picker.IsLoading);
        Assert.Equal("Steam", rows["Skyrim"].StoreLabel);
        Assert.Equal("GOG Galaxy", rows["Deus Ex"].StoreLabel);
        Assert.Equal(@"Installed at C:\Games\Skyrim", rows["Skyrim"].InstallLine);
        Assert.Equal(@"Saves folder: C:\Users\you\Documents\My Games\Skyrim\Saves", rows["Skyrim"].SavesLine);
        Assert.StartsWith("Saves folder unknown", rows["Deus Ex"].SavesLine);
    }

    [Fact]
    public async Task A_rows_screen_reader_name_holds_the_game_the_store_the_status_and_where_it_is_installed()
    {
        var (picker, _) = await LoadedAsync(LibraryTestData.Scan(LibraryTestData.Game("Portal 2", id: "620", addedGameId: "p2"), LibraryTestData.Game("Portal 2", LibrarySource.Gog, "9")));

        var names = picker.VisibleRows.Select(row => row.AutomationName).ToList();

        Assert.Contains(@"Portal 2, Steam, Already added, installed at C:\Games\Portal 2", names);
        Assert.Contains(@"Portal 2, GOG Galaxy, installed at C:\Games\Portal 2", names);
    }

    [Fact]
    public async Task A_saves_folder_the_game_has_not_made_yet_says_it_is_left_out()
    {
        var game = LibraryTestData.Game("Skyrim", saves: @"C:\Docs\Skyrim\Saves", savesExist: false);
        var (picker, _) = await LoadedAsync(LibraryTestData.Scan(game));

        Assert.Contains("doesn't exist yet", picker.VisibleRows.Single().SavesLine);
    }

    [Fact]
    public async Task Nothing_is_checked_until_the_player_checks_it()
    {
        var (picker, _) = await LoadedAsync(LibraryTestData.Scan(LibraryTestData.Game("A"), LibraryTestData.Game("B")));

        Assert.Equal(0, picker.SelectedCount);
        Assert.False(picker.AddCommand.CanExecute(null));
        Assert.Equal("Add games", picker.AddLabel);
        Assert.Equal("0 of 2 selected", picker.CountText);
    }

    [Fact]
    public async Task A_game_that_is_already_added_says_so_and_cannot_be_checked()
    {
        var (picker, _) = await LoadedAsync(LibraryTestData.Scan(LibraryTestData.Game("A", addedGameId: "a-1"), LibraryTestData.Game("B")));
        var added = picker.VisibleRows.Single(row => row.Name == "A");

        added.IsSelected = true;
        picker.SelectAllCommand.Execute(null);

        Assert.True(added.IsAlreadyAdded);
        Assert.False(added.CanSelect);
        Assert.False(added.IsSelected);
        Assert.Equal("Already added", added.StatusText);
        Assert.Equal(1, picker.SelectedCount);
        Assert.Equal("1 of 1 selected", picker.CountText);
    }

    [Fact]
    public async Task Select_all_checks_every_game_that_can_be_added_and_select_none_clears_them()
    {
        var (picker, _) = await LoadedAsync(LibraryTestData.Scan(LibraryTestData.Game("A"), LibraryTestData.Game("B"), LibraryTestData.Game("C")));

        picker.SelectAllCommand.Execute(null);
        var all = picker.SelectedCount;
        var label = picker.AddLabel;
        picker.SelectNoneCommand.Execute(null);

        Assert.Equal(3, all);
        Assert.Equal("Add 3 games", label);
        Assert.Equal(0, picker.SelectedCount);
    }

    [Fact]
    public async Task One_checked_game_reads_in_the_singular()
    {
        var (picker, _) = await LoadedAsync(LibraryTestData.Scan(LibraryTestData.Game("A"), LibraryTestData.Game("B")));

        picker.VisibleRows[0].IsSelected = true;

        Assert.Equal("Add 1 game", picker.AddLabel);
        Assert.True(picker.AddCommand.CanExecute(null));
    }

    [Fact]
    public async Task Searching_narrows_the_list_by_name_or_folder_ignoring_case_and_select_all_follows_it()
    {
        var (picker, _) = await LoadedAsync(LibraryTestData.Scan(LibraryTestData.Game("Skyrim"), LibraryTestData.Game("Fallout 4"), LibraryTestData.Game("Starfield")));

        picker.SearchText = "FALL";
        picker.SelectAllCommand.Execute(null);
        var byName = Visible(picker);
        picker.SearchText = @"games\sky";
        var byFolder = Visible(picker);

        Assert.Equal(["Fallout 4"], byName);
        Assert.Equal(["Skyrim"], byFolder);
        Assert.Equal(1, picker.SelectedCount);
    }

    [Fact]
    public async Task A_search_that_finds_nothing_says_so()
    {
        var (picker, _) = await LoadedAsync(LibraryTestData.Scan(LibraryTestData.Game("Skyrim")));

        picker.SearchText = "zzz";

        Assert.Empty(picker.VisibleRows);
        Assert.True(picker.ShowNoMatches);
        Assert.False(picker.ShowEmpty);
    }

    [Fact]
    public async Task Clearing_a_search_that_finds_nothing_lists_the_games_again()
    {
        var (picker, _) = await LoadedAsync(LibraryTestData.Scan(LibraryTestData.Game("Skyrim"), LibraryTestData.Game("Fallout 4")));
        picker.SearchText = "zzz";
        Assert.True(picker.ShowClearSearch);

        picker.ClearSearchCommand.Execute(null);

        Assert.Equal(string.Empty, picker.SearchText);
        Assert.False(picker.ShowClearSearch);
        Assert.Equal(["Fallout 4", "Skyrim"], Visible(picker));
        Assert.Equal("Clear search", picker.ClearSearchName);
    }

    [Fact]
    public async Task Nothing_to_clear_is_offered_when_no_game_is_left_by_hiding_the_added_ones()
    {
        var (picker, _) = await LoadedAsync(LibraryTestData.Scan(LibraryTestData.Game("A", addedGameId: "a-1")));

        picker.HideAdded = true;

        Assert.True(picker.ShowNoMatches);
        Assert.False(picker.ShowClearSearch);
    }

    [Fact]
    public async Task Hiding_the_added_games_when_every_game_is_added_says_so_and_points_at_the_check_box()
    {
        var (picker, _) = await LoadedAsync(LibraryTestData.Scan(LibraryTestData.Game("A", addedGameId: "a-1")));

        picker.HideAdded = true;

        Assert.Equal("Every game is already added. Turn off \"Hide games already added\" to see them.", picker.NoMatchesText);
    }

    [Fact]
    public async Task A_search_that_finds_nothing_still_says_no_games_match_while_the_added_ones_are_hidden()
    {
        var (picker, _) = await LoadedAsync(LibraryTestData.Scan(LibraryTestData.Game("A", addedGameId: "a-1"), LibraryTestData.Game("B")));
        picker.HideAdded = true;

        picker.SearchText = "zzz";

        Assert.Equal("No games match your search.", picker.NoMatchesText);
    }

    [Fact]
    public async Task Hiding_the_games_already_added_leaves_only_the_new_ones()
    {
        var (picker, _) = await LoadedAsync(LibraryTestData.Scan(LibraryTestData.Game("A", addedGameId: "a-1"), LibraryTestData.Game("B")));

        picker.HideAdded = true;

        Assert.Equal(["B"], Visible(picker));
    }

    [Fact]
    public async Task Games_are_sorted_by_name_unless_the_player_sorts_by_store_or_by_known_saves()
    {
        var steamB = LibraryTestData.Game("b game");
        var steamC = LibraryTestData.Game("C Game", saves: @"C:\Saves\C");
        var gogA = LibraryTestData.Game("A Game", LibrarySource.Gog, "5");
        var (picker, _) = await LoadedAsync(LibraryTestData.Scan(steamB, steamC, gogA));

        var byName = Visible(picker);
        picker.Sort = picker.SortChoices.Single(choice => choice.Value == LibrarySort.Store);
        var byStore = Visible(picker);
        picker.Sort = picker.SortChoices.Single(choice => choice.Value == LibrarySort.SavesKnown);
        var bySaves = Visible(picker);

        Assert.Equal(["A Game", "b game", "C Game"], byName);
        Assert.Equal(["b game", "C Game", "A Game"], byStore);
        Assert.Equal(["C Game", "A Game", "b game"], bySaves);
    }

    [Fact]
    public async Task A_selection_survives_a_search_and_is_counted_in_full()
    {
        var (picker, _) = await LoadedAsync(LibraryTestData.Scan(LibraryTestData.Game("Skyrim"), LibraryTestData.Game("Fallout 4")));
        picker.VisibleRows.Single(row => row.Name == "Skyrim").IsSelected = true;

        picker.SearchText = "fall";

        Assert.Equal(1, picker.SelectedCount);
        picker.AddCommand.Execute(null);
        Assert.Equal(["Skyrim"], (await picker.Result)!.Select(game => game.Name));
    }

    [Fact]
    public async Task Adding_answers_with_the_checked_games_in_scan_order()
    {
        var (picker, _) = await LoadedAsync(LibraryTestData.Scan(LibraryTestData.Game("Zed"), LibraryTestData.Game("Alpha"), LibraryTestData.Game("Mid")));
        picker.SearchText = string.Empty;
        foreach (var row in picker.VisibleRows.Where(row => row.Name != "Mid"))
        {
            row.IsSelected = true;
        }

        picker.AddCommand.Execute(null);

        Assert.True(picker.Result.IsCompleted);
        Assert.Equal(["Zed", "Alpha"], (await picker.Result)!.Select(game => game.Name));
    }

    [Fact]
    public async Task Cancel_and_dismiss_answer_with_nothing()
    {
        var (first, _) = await LoadedAsync(LibraryTestData.Scan(LibraryTestData.Game("A")));
        var (second, _) = await LoadedAsync(LibraryTestData.Scan(LibraryTestData.Game("A")));

        first.CancelCommand.Execute(null);
        second.RequestDismiss();

        Assert.Null(await first.Result);
        Assert.Null(await second.Result);
    }

    [Fact]
    public async Task While_the_scan_runs_the_dialog_says_it_is_looking()
    {
        var backend = FakeBackend.Empty();
        var picker = new LibraryPickerDialogViewModel(backend.Games, LibraryTestData.Text);

        Assert.True(picker.IsLoading);
        Assert.Equal("Looking for your installed games…", picker.LoadingText);
        await picker.LoadAsync();
        Assert.False(picker.IsLoading);
    }

    [Fact]
    public async Task A_library_with_no_games_says_which_stores_were_not_found()
    {
        var (picker, _) = await LoadedAsync(null);

        Assert.True(picker.ShowEmpty);
        Assert.Equal("No installed games were found.", picker.EmptyText);
        Assert.Equal(
            ["Steam wasn't found on this computer, so no Steam games are listed.", "GOG Galaxy's list of installed games wasn't found, so no GOG games are listed."],
            picker.StoreNotes);
        Assert.False(picker.AddCommand.CanExecute(null));
    }

    [Fact]
    public async Task A_store_that_was_found_adds_no_note()
    {
        var scan = new LibraryScan([LibraryTestData.Game("A")], true, false);
        var (picker, _) = await LoadedAsync(scan);

        Assert.Equal(["GOG Galaxy's list of installed games wasn't found, so no GOG games are listed."], picker.StoreNotes);
        Assert.False(picker.ShowEmpty);
    }

    [Fact]
    public async Task A_scan_that_fails_shows_why_and_lists_nothing()
    {
        var (picker, _) = await LoadedAsync(
            LibraryTestData.Scan(LibraryTestData.Game("A")),
            backend => backend.FailNext<IGamesApi>(nameof(IGamesApi.ScanLibrariesAsync), ErrorInfo.From(new CatalogCorruptException("games.json", null))));

        Assert.True(picker.HasError);
        Assert.StartsWith("SavePoint couldn't look through your libraries.", picker.ErrorText);
        Assert.Empty(picker.VisibleRows);
        Assert.False(picker.IsLoading);
    }

    [Theory]
    [InlineData("cancel")]
    [InlineData("dismiss")]
    public async Task Closing_the_dialog_cancels_the_scan_it_started(string how)
    {
        var backend = FakeBackend.Empty();
        var picker = new LibraryPickerDialogViewModel(backend.Games, LibraryTestData.Text);
        await picker.LoadAsync();
        var token = backend.Games.ScanTokens.Single();
        Assert.False(token.IsCancellationRequested);

        if (how == "cancel")
        {
            picker.CancelCommand.Execute(null);
        }
        else
        {
            picker.RequestDismiss();
        }

        Assert.True(token.IsCancellationRequested);
    }

    [Fact]
    public async Task A_scan_that_closing_the_dialog_cancelled_shows_no_error()
    {
        var backend = FakeBackend.Empty();
        var picker = new LibraryPickerDialogViewModel(backend.Games, LibraryTestData.Text);
        backend.Games.ScanGate = async token =>
        {
            picker.CancelCommand.Execute(null);
            await Task.CompletedTask;
        };

        await picker.LoadAsync();

        Assert.False(picker.HasError);
        Assert.Empty(picker.ErrorText);
    }

    [Fact]
    public async Task The_scan_is_one_read_that_changes_nothing()
    {
        var (picker, backend) = await LoadedAsync(LibraryTestData.Scan(LibraryTestData.Game("A")));

        // The game is offered, none is selected, and the library still has no game: looking changed nothing. The scan is the only
        // thing asked of the games Api, and only once: the call count is what "one read" promises.
        Assert.Equal<string>([nameof(IGamesApi.ScanLibrariesAsync)], backend.Calls.All.Where(call => call.Api == nameof(IGamesApi)).Select(call => call.Member));
        Assert.Equal(["A"], picker.VisibleRows.Select(row => row.Name));
        Assert.Equal(0, picker.SelectedCount);
        Assert.Empty(backend.State.Games);
    }

    [Fact]
    public async Task The_dialog_describes_itself_for_assistive_technology_and_pins_its_buttons()
    {
        var (picker, _) = await LoadedAsync(LibraryTestData.Scan(LibraryTestData.Game("A")));

        Assert.Equal("Add games from your libraries", picker.Title);
        Assert.Contains("Check the games you want to add", picker.Description);
        Assert.True(picker.PinsFooter);
    }
}
