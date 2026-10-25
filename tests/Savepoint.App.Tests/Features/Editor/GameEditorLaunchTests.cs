using Savepoint.App.Features.Editor;
using Savepoint.App.Tests.Fakes;
using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;

namespace Savepoint.App.Tests.Features.Editor;

public class GameEditorLaunchTests
{
    // Paths for the browse tests follow the operating system the test runs on, because turning a pick into a relative path is the system's own.
    private static readonly string _root = OperatingSystem.IsWindows() ? @"C:\Games\SkyrimSE" : "/games/SkyrimSE";

    private static string Under(params string[] parts) => Path.Combine([_root, .. parts]);

    private static string Elsewhere => OperatingSystem.IsWindows() ? @"D:\Other\Game.exe" : "/other/Game.exe";

    private static EditorRig RigWithExecutables(params string[] executables) =>
        EditorRig.Create(new FakeBackend(ScenarioBuilder.Mockup().WithExecutables(MockupScenario.SkyrimInstall, executables)));

    private static EditorRig BrowseRig() =>
        EditorRig.Create(new FakeBackend(ScenarioBuilder.Empty().WithGame(MockupScenario.Skyrim with { Install = new InstallLocation(_root) })));

    [Theory]
    [InlineData("windows", "Run the .exe", "Running the .exe avoids that.")]
    [InlineData("macos", "Run the app", "Running the app avoids that.")]
    [InlineData("linux", "Run the program", "Running the program avoids that.")]
    public void The_start_choice_names_the_kind_of_program_the_operating_system_runs(string os, string label, string warningEnd)
    {
        var editor = EditorRig.Create(os: os).New();

        Assert.Equal(label, editor.RunExeLabel);
        Assert.StartsWith("Starting through Steam can let Steam update the game again right after SavePoint puts your version back.", editor.SteamWarning);
        Assert.EndsWith(warningEnd, editor.SteamWarning);
    }

    [Fact]
    public void The_method_flags_follow_the_choice()
    {
        var editor = EditorRig.Create().New();
        Assert.True(editor.IsExeMethod);
        Assert.False(editor.IsSteamMethod);

        editor.LaunchChoice = 1;

        Assert.False(editor.IsExeMethod);
        Assert.True(editor.IsSteamMethod);
    }

    [Fact]
    public async Task Programs_found_in_the_install_folder_are_suggested_except_the_one_already_chosen()
    {
        var editor = RigWithExecutables("SkyrimSELauncher.exe", "SkyrimSE.exe", @"bin\tools\Helper.exe").Edit();

        await editor.LoadAsync();

        Assert.Equal(["SkyrimSE.exe", @"bin\tools\Helper.exe"], editor.ExeSuggestions.Select(item => item.Text));
    }

    [Fact]
    public async Task What_is_typed_narrows_the_suggestions_ignoring_case()
    {
        var editor = RigWithExecutables("SkyrimSELauncher.exe", "SkyrimSE.exe", @"bin\tools\Helper.exe").Edit();
        await editor.LoadAsync();

        editor.ExePath = "HELPER";

        Assert.Equal([@"bin\tools\Helper.exe"], editor.ExeSuggestions.Select(item => item.Text));
    }

    [Fact]
    public async Task Using_a_suggestion_fills_the_field_and_removes_it_from_the_list()
    {
        var editor = RigWithExecutables("SkyrimSELauncher.exe", "SkyrimSE.exe").Edit();
        await editor.LoadAsync();

        editor.ExeSuggestions[0].Use.Execute(null);

        Assert.Equal("SkyrimSE.exe", editor.ExePath);
        Assert.Equal(["SkyrimSELauncher.exe"], editor.ExeSuggestions.Select(item => item.Text));
    }

    [Fact]
    public async Task Only_a_handful_of_suggestions_are_shown()
    {
        var editor = RigWithExecutables([.. Enumerable.Range(1, 12).Select(number => $"tool{number:00}.exe")]).New();
        editor.InstallPath = MockupScenario.SkyrimInstall;

        await editor.RefreshSuggestionsAsync();

        Assert.Equal(8, editor.ExeSuggestions.Count);
        Assert.Equal(6, editor.ProcessSuggestions.Count);
    }

    [Fact]
    public async Task The_process_name_is_suggested_from_the_file_names_without_repeats()
    {
        var editor = RigWithExecutables("SkyrimSELauncher.exe", "SkyrimSE.exe", @"bin\tools\Helper.exe", @"other\Helper.exe").Edit();

        await editor.LoadAsync();

        Assert.Equal(["SkyrimSELauncher.exe", "Helper.exe"], editor.ProcessSuggestions.Select(item => item.Text));

        editor.ProcessSuggestions[0].Use.Execute(null);

        Assert.Equal("SkyrimSELauncher.exe", editor.ProcessName);
        Assert.Equal(["SkyrimSE.exe", "Helper.exe"], editor.ProcessSuggestions.Select(item => item.Text));
    }

    [Fact]
    public async Task The_program_list_is_asked_for_with_the_games_name_so_the_service_can_rank_by_it()
    {
        var rig = RigWithExecutables("SkyrimSELauncher.exe", "SkyrimSE.exe");
        var editor = rig.Edit();
        await editor.LoadAsync();

        var asked = Assert.Single(rig.Backend.Calls.To<IGamesApi>("ListExecutablesAsync"));

        Assert.Equal(editor.Name.Trim(), asked.Args[1]);
        Assert.NotEqual(string.Empty, asked.Args[1]);
    }

    [Fact]
    public async Task A_program_the_player_already_chose_stays_when_the_suggestions_are_refreshed()
    {
        var rig = RigWithExecutables("SkyrimSELauncher.exe", "SkyrimSE.exe");
        var editor = rig.Edit();
        await editor.LoadAsync();
        editor.ExePath = "Chosen.exe";
        var install = editor.InstallPath;

        editor.InstallPath = @"D:\Games\Other";
        await editor.RefreshSuggestionsAsync();
        editor.InstallPath = install;
        await editor.RefreshSuggestionsAsync();

        Assert.Equal(["SkyrimSELauncher.exe", "SkyrimSE.exe"], editor.ExeSuggestions.Select(item => item.Text));
        Assert.Equal("Chosen.exe", editor.ExePath);
    }

    [Fact]
    public async Task Changing_the_install_folder_looks_again_and_an_empty_one_suggests_nothing()
    {
        var rig = RigWithExecutables("SkyrimSE.exe");
        var editor = rig.Edit();
        await editor.LoadAsync();
        rig.Backend.Calls.Clear();

        editor.InstallPath = @"D:\Games\Other";
        await editor.RefreshSuggestionsAsync();

        Assert.Equal(@"D:\Games\Other", Assert.Single(rig.Backend.Calls.To<IGamesApi>("ListExecutablesAsync")).Args[0]);

        editor.InstallPath = "  ";
        await editor.RefreshSuggestionsAsync();

        Assert.Empty(editor.ExeSuggestions);
        Assert.Single(rig.Backend.Calls.To<IGamesApi>("ListExecutablesAsync"));
    }

    [Fact]
    public async Task A_folder_that_was_already_looked_at_is_not_scanned_again_until_it_changes()
    {
        var rig = RigWithExecutables("SkyrimSE.exe");
        var editor = rig.Edit();
        await editor.LoadAsync();

        await editor.RefreshSuggestionsAsync();
        await editor.RefreshSuggestionsAsync();

        Assert.Single(rig.Backend.Calls.To<IGamesApi>("ListExecutablesAsync"));

        editor.InstallPath = @"D:\Games\Other";
        await editor.RefreshSuggestionsAsync();
        await editor.RefreshSuggestionsAsync();

        Assert.Equal(2, rig.Backend.Calls.To<IGamesApi>("ListExecutablesAsync").Count);
    }

    private static GameEditorViewModel LinkEditor(EditorRig rig)
    {
        var editor = rig.New();
        editor.InstallPath = MockupScenario.SkyrimInstall;
        return editor;
    }

    [Fact]
    public async Task Choosing_a_launch_link_offers_the_games_own_program_as_the_process_name()
    {
        var rig = RigWithExecutables("SkyrimSELauncher.exe", "SkyrimSE.exe", @"bin\tools\Helper.exe");
        var editor = LinkEditor(rig);

        editor.LaunchChoice = 1;
        await editor.RefreshProcessNameSuggestionAsync();

        Assert.Equal("SkyrimSE.exe", editor.SuggestedProcessName);
        Assert.True(editor.HasProcessNameSuggestion);
        Assert.True(editor.ShowProcessNameWhy);
        Assert.Equal("Use SkyrimSE.exe", editor.UseSuggestedProcessNameLabel);
    }

    [Fact]
    public async Task Using_the_suggestion_fills_the_process_name_and_clears_the_offer()
    {
        var rig = RigWithExecutables("SkyrimSE.exe");
        var editor = LinkEditor(rig);
        editor.LaunchChoice = 1;
        await editor.RefreshProcessNameSuggestionAsync();

        editor.UseSuggestedProcessNameCommand.Execute(null);

        Assert.Equal("SkyrimSE.exe", editor.ProcessName);
        Assert.False(editor.HasProcessNameSuggestion);
        Assert.False(editor.ShowProcessNameWhy);
    }

    [Fact]
    public async Task Nothing_is_offered_while_the_game_starts_from_a_file_and_the_folder_is_not_asked()
    {
        var rig = RigWithExecutables("SkyrimSE.exe");
        var editor = LinkEditor(rig);

        await editor.RefreshProcessNameSuggestionAsync();

        Assert.False(editor.HasProcessNameSuggestion);
        Assert.False(editor.ShowProcessNameWhy);
        Assert.Empty(rig.Backend.Calls.To<IGamesApi>("SuggestProcessNameAsync"));
    }

    [Fact]
    public async Task Nothing_is_offered_when_a_process_name_is_already_typed()
    {
        var rig = RigWithExecutables("SkyrimSE.exe");
        var editor = LinkEditor(rig);
        editor.ProcessName = "Game.exe";
        editor.LaunchChoice = 1;

        await editor.RefreshProcessNameSuggestionAsync();

        Assert.False(editor.HasProcessNameSuggestion);
        Assert.False(editor.ShowProcessNameWhy);
        Assert.Empty(rig.Backend.Calls.To<IGamesApi>("SuggestProcessNameAsync"));
    }

    [Fact]
    public async Task A_folder_with_no_game_program_still_explains_why_the_name_matters_but_offers_no_button()
    {
        var rig = RigWithExecutables("SkyrimSELauncher.exe");
        var editor = LinkEditor(rig);
        editor.LaunchChoice = 1;

        await editor.RefreshProcessNameSuggestionAsync();

        Assert.True(editor.ShowProcessNameWhy);
        Assert.False(editor.HasProcessNameSuggestion);
        Assert.Equal(string.Empty, editor.SuggestedProcessName);
    }

    [Fact]
    public async Task The_explanation_is_shown_before_a_folder_is_chosen_and_the_folder_is_not_asked()
    {
        var rig = RigWithExecutables("SkyrimSE.exe");
        var editor = rig.New();
        editor.LaunchChoice = 1;

        await editor.RefreshProcessNameSuggestionAsync();

        Assert.True(editor.ShowProcessNameWhy);
        Assert.False(editor.HasProcessNameSuggestion);
        Assert.Empty(rig.Backend.Calls.To<IGamesApi>("SuggestProcessNameAsync"));
    }

    [Fact]
    public async Task A_new_install_folder_is_asked_again_for_a_link_started_game()
    {
        var rig = RigWithExecutables("SkyrimSE.exe");
        var editor = LinkEditor(rig);
        editor.LaunchChoice = 1;
        await editor.RefreshProcessNameSuggestionAsync();

        editor.InstallPath = @"D:\Games\Other";
        await editor.RefreshSuggestionsAsync();

        Assert.Equal(@"D:\Games\Other", rig.Backend.Calls.To<IGamesApi>("SuggestProcessNameAsync").Last().Args[0]);
        Assert.False(editor.HasProcessNameSuggestion);
    }

    [Fact]
    public async Task Clearing_the_install_folder_takes_the_offer_away_at_once()
    {
        var rig = RigWithExecutables("SkyrimSE.exe");
        var editor = LinkEditor(rig);
        editor.LaunchChoice = 1;
        await editor.RefreshProcessNameSuggestionAsync();
        Assert.True(editor.HasProcessNameSuggestion);

        editor.InstallPath = string.Empty;

        Assert.False(editor.HasProcessNameSuggestion);
        Assert.Equal(string.Empty, editor.SuggestedProcessName);
    }

    [Fact]
    public async Task Choosing_another_folder_takes_the_old_offer_away_before_the_new_look_finishes()
    {
        var rig = RigWithExecutables("SkyrimSE.exe");
        var editor = LinkEditor(rig);
        editor.LaunchChoice = 1;
        await editor.RefreshProcessNameSuggestionAsync();

        editor.InstallPath = @"D:\Games\Other";

        Assert.False(editor.HasProcessNameSuggestion);
        Assert.Equal(string.Empty, editor.SuggestedProcessName);
    }

    [Fact]
    public async Task A_late_answer_for_an_old_folder_does_not_replace_the_newer_offer()
    {
        const string other = @"D:\Games\Other";
        var rig = EditorRig.Create(new FakeBackend(ScenarioBuilder.Mockup()
            .WithExecutables(MockupScenario.SkyrimInstall, "SkyrimSE.exe")
            .WithExecutables(other, "Other.exe")));
        var editor = rig.New();
        editor.LaunchChoice = 1;
        var held = new TaskCompletionSource();
        rig.Backend.Games.SuggestGate = (folder, _) => folder == MockupScenario.SkyrimInstall ? held.Task : Task.CompletedTask;

        editor.InstallPath = MockupScenario.SkyrimInstall;
        var first = editor.RefreshProcessNameSuggestionAsync();
        editor.InstallPath = other;
        await editor.RefreshProcessNameSuggestionAsync();
        held.SetResult();
        await first;

        Assert.Equal("Other.exe", editor.SuggestedProcessName);
        Assert.True(editor.HasProcessNameSuggestion);
    }

    [Fact]
    public async Task Choosing_another_folder_cancels_the_look_in_the_old_one()
    {
        var rig = RigWithExecutables("SkyrimSE.exe");
        var editor = rig.New();
        editor.LaunchChoice = 1;
        var held = new TaskCompletionSource();
        rig.Backend.Games.SuggestGate = (_, _) => held.Task;
        editor.InstallPath = MockupScenario.SkyrimInstall;
        var first = editor.RefreshProcessNameSuggestionAsync();
        var token = Assert.Single(rig.Backend.Games.SuggestTokens);
        Assert.False(token.IsCancellationRequested);

        editor.InstallPath = @"D:\Games\Other";
        rig.Backend.Games.SuggestGate = null;
        await editor.RefreshSuggestionsAsync();
        held.SetResult();
        await first;

        Assert.True(token.IsCancellationRequested);
    }

    [Fact]
    public async Task A_suggestion_that_cannot_be_read_offers_nothing_and_shows_no_error()
    {
        var rig = RigWithExecutables("SkyrimSE.exe");
        rig.Backend.FailNext<IGamesApi>("SuggestProcessNameAsync", new ErrorInfo("error.unknown", new Dictionary<string, object?>(), string.Empty));
        var editor = LinkEditor(rig);
        // Choosing the link asks once, and that one answer is the failure.
        editor.LaunchChoice = 1;

        Assert.Single(rig.Backend.Calls.To<IGamesApi>("SuggestProcessNameAsync"));
        Assert.False(editor.HasProcessNameSuggestion);
        Assert.True(editor.ShowProcessNameWhy);
        Assert.Empty(rig.Toasts.Toasts);
        Assert.Null(editor.FormError);
    }

    [Fact]
    public async Task Going_back_to_a_file_start_hides_the_offer()
    {
        var rig = RigWithExecutables("SkyrimSE.exe");
        var editor = LinkEditor(rig);
        editor.LaunchChoice = 1;
        await editor.RefreshProcessNameSuggestionAsync();

        editor.LaunchChoice = 0;

        Assert.False(editor.HasProcessNameSuggestion);
        Assert.False(editor.ShowProcessNameWhy);
    }

    [Fact]
    public async Task A_scan_that_fails_suggests_nothing_and_shows_no_error()
    {
        var rig = RigWithExecutables("SkyrimSE.exe");
        rig.Backend.FailNext<IGamesApi>("ListExecutablesAsync", new ErrorInfo("error.unknown", new Dictionary<string, object?>(), string.Empty));
        var editor = rig.Edit();

        await editor.LoadAsync();

        Assert.Empty(editor.ExeSuggestions);
        Assert.Empty(rig.Toasts.Toasts);
        Assert.Null(editor.FormError);
    }

    [Fact]
    public async Task A_picked_file_inside_the_install_folder_becomes_a_relative_path()
    {
        var rig = BrowseRig();
        var editor = rig.Edit();
        rig.Dialogs.AnswerOpenFile(Under("bin", "Win64", "Game.exe"));

        await editor.BrowseExeCommand.ExecuteAsync(null);

        Assert.Equal(Path.Combine("bin", "Win64", "Game.exe"), editor.ExePath);
        Assert.Null(editor.ExePathError);
        Assert.Equal(_root, Assert.Single(rig.Dialogs.OpenFilePicks).StartPath);
    }

    [Theory]
    [InlineData("elsewhere")]
    [InlineData("sibling")]
    [InlineData("parent")]
    public async Task A_picked_file_outside_the_install_folder_is_refused_with_copy_and_the_field_is_kept(string kind)
    {
        var rig = BrowseRig();
        var editor = rig.Edit();
        var picked = kind switch
        {
            "elsewhere" => Elsewhere,
            "sibling" => Path.Combine(Path.GetDirectoryName(_root)!, "SkyrimSEOld", "Game.exe"),
            _ => Path.Combine(Path.GetDirectoryName(_root)!, "Game.exe"),
        };
        rig.Dialogs.AnswerOpenFile(picked);

        await editor.BrowseExeCommand.ExecuteAsync(null);

        Assert.Equal("SkyrimSELauncher.exe", editor.ExePath);
        Assert.NotNull(editor.ExePathError);
        Assert.StartsWith("That file isn't inside the game's install folder.", editor.ExePathError);
        Assert.Contains(_root, editor.ExePathError);
    }

    [Fact]
    public async Task Cancelling_the_file_picker_changes_nothing()
    {
        var rig = BrowseRig();
        var editor = rig.Edit();

        await editor.BrowseExeCommand.ExecuteAsync(null);

        Assert.Equal("SkyrimSELauncher.exe", editor.ExePath);
        Assert.Null(editor.ExePathError);
    }

    [Fact]
    public async Task Browsing_for_the_start_file_needs_the_install_folder_first()
    {
        var rig = EditorRig.Create();
        var editor = rig.New();

        await editor.BrowseExeCommand.ExecuteAsync(null);

        Assert.Equal("Choose the install folder first, so the start file can be shown relative to it.", editor.ExePathError);
        Assert.Empty(rig.Dialogs.OpenFilePicks);
    }
}
