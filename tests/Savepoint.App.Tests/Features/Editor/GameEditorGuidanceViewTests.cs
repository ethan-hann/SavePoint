using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using Savepoint.App.Tests.Fakes;
using Savepoint.Core.Api;

namespace Savepoint.App.Tests.Features.Editor;

// The editor's warnings and explanations appear only where they apply, and every explanation sits behind a question button.
public class GameEditorGuidanceViewTests
{
    private const string _antiCheatText = "Don't use SavePoint with online games that have anti-cheat. Changed game files can get your account flagged.";
    private const string _unknownText = "If this game's store syncs cloud saves, it can bring back saves that SavePoint removed. Turn off cloud saves for this game in its store to be safe.";
    private const string _steamCloudStart = "Steam Cloud is on for this game.";

    private static ToggleButton? Question(EditorViewHarness harness, string question) =>
        harness.All<ToggleButton>().FirstOrDefault(button => AutomationProperties.GetName(button) == question);

    private static bool Shows(EditorViewHarness harness, Func<string, bool> match) =>
        harness.All<TextBlock>().Any(block => block.IsEffectivelyVisible && block.Text is { } text && match(text));

    private static void Click(EditorViewHarness harness, ToggleButton? button)
    {
        Assert.NotNull(button);
        button.IsChecked = true;
        harness.Settle();
    }

    private static async Task<EditorViewHarness> OpenWithAsync(CloudSaveInfo info)
    {
        var harness = await EditorViewHarness.StartAsync();
        Assert.IsType<FakeGamesApi>(harness.Backend.Games).CloudSaves[SampleScenario.SkyrimId] = info;
        var editor = harness.Edit();
        await editor.LoadAsync();
        harness.Settle();
        return harness;
    }

    [AvaloniaFact]
    public async Task The_Steam_warning_shows_only_when_Through_Steam_is_chosen()
    {
        using var harness = await EditorViewHarness.StartAsync();
        var editor = harness.Edit();
        var callout = harness.Find<Border>("SteamWarningCallout");
        Assert.False(callout.IsEffectivelyVisible);

        editor.LaunchChoice = 1;
        harness.Settle();
        Assert.True(callout.IsEffectivelyVisible);

        editor.LaunchChoice = 0;
        harness.Settle();
        Assert.False(callout.IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public async Task The_More_settings_toggle_tells_screen_readers_whether_it_is_expanded()
    {
        using var harness = await EditorViewHarness.StartAsync();
        var editor = harness.New();
        harness.Settle();
        var toggle = harness.Find<ToggleButton>("MoreSettingsToggle");

        var provider = Assert.IsAssignableFrom<Avalonia.Automation.Provider.IExpandCollapseProvider>(
            Avalonia.Automation.Peers.ControlAutomationPeer.CreatePeerForElement(toggle));
        Assert.Equal(Avalonia.Automation.ExpandCollapseState.Collapsed, provider.ExpandCollapseState);

        editor.IsMoreSettingsOpen = true;
        harness.Settle();

        Assert.Equal(Avalonia.Automation.ExpandCollapseState.Expanded, provider.ExpandCollapseState);
    }

    [AvaloniaFact]
    public async Task The_nudge_to_More_settings_shows_for_a_link_game_with_no_program_to_suggest_and_opens_the_field()
    {
        using var harness = await EditorViewHarness.StartAsync();
        var editor = harness.New();
        editor.LaunchChoice = 1;
        harness.Settle();
        var button = harness.Find<Button>("OpenProcessFieldButton");

        Assert.True(button.IsEffectivelyVisible);
        Assert.True(harness.Find<TextBlock>("ProcessMoreSettingsText").IsEffectivelyVisible);

        button.Command!.Execute(null);
        harness.Settle();

        Assert.True(harness.Find<Control>("MoreSettingsContent").IsEffectivelyVisible);
        Assert.Equal("Knowing when you've stopped playing", FocusedField(harness));
    }

    private static string? FocusedField(EditorViewHarness harness) =>
        harness.Window.FocusManager!.GetFocusedElement() is Control focused ? AutomationProperties.GetName(focused) : null;

    [AvaloniaFact]
    public async Task On_Linux_the_note_that_prefers_Through_Steam_and_the_Steam_warning_never_show_together()
    {
        using var harness = await EditorViewHarness.StartAsync(os: "linux");
        var editor = harness.New();
        editor.InstallPath = "/home/p/.local/share/Steam/steamapps/common/Game";
        harness.Settle();
        var note = harness.Find<TextBlock>("SteamStartNoteText");
        var warning = harness.Find<Border>("SteamWarningCallout");

        Assert.True(note.IsEffectivelyVisible);
        Assert.False(warning.IsEffectivelyVisible);

        editor.LaunchChoice = 1;
        harness.Settle();

        Assert.False(note.IsEffectivelyVisible);
        Assert.True(warning.IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public async Task The_anti_cheat_line_sits_behind_a_question_button()
    {
        using var harness = await EditorViewHarness.StartAsync();
        harness.Edit();

        Assert.False(Shows(harness, text => text == _antiCheatText));
        Click(harness, Question(harness, "Can I use this with online games?"));

        Assert.True(Shows(harness, text => text == _antiCheatText));
    }

    [AvaloniaFact]
    public async Task Cloud_saves_on_show_a_warning_callout_under_the_saves_folder_and_no_question()
    {
        using var harness = await OpenWithAsync(new CloudSaveInfo(CloudSaveState.On, CloudStore.Steam));

        var callout = harness.All<Border>().FirstOrDefault(border => border.Name == "CloudSavesCallout");
        Assert.NotNull(callout);
        Assert.True(callout.IsEffectivelyVisible);
        Assert.True(callout.Classes.Contains("callout") && callout.Classes.Contains("warning"));
        Assert.True(Shows(harness, text => text.StartsWith(_steamCloudStart, StringComparison.Ordinal)));
        Assert.False(Question(harness, "Does my store sync saves?")?.IsEffectivelyVisible ?? false);
        var savesBottom = harness.Find<TextBox>("SavesField").TranslatePoint(new Point(0, harness.Find<TextBox>("SavesField").Bounds.Height), harness.View)!.Value.Y;
        var calloutTop = callout.TranslatePoint(new Point(0, 0), harness.View)!.Value.Y;
        Assert.True(calloutTop >= savesBottom, $"The callout starts at {calloutTop:0} but the saves folder ends at {savesBottom:0}.");
    }

    [AvaloniaFact]
    public async Task Cloud_saves_unknown_show_a_question_button_that_reveals_the_text()
    {
        using var harness = await OpenWithAsync(new CloudSaveInfo(CloudSaveState.Unknown, CloudStore.None));

        Assert.DoesNotContain(harness.All<Border>(), border => border.Name == "CloudSavesCallout" && border.IsEffectivelyVisible);
        Assert.False(Shows(harness, text => text == _unknownText));
        var button = Question(harness, "Does my store sync saves?");
        Assert.NotNull(button);
        Assert.True(button.IsEffectivelyVisible);

        Click(harness, button);

        Assert.True(Shows(harness, text => text == _unknownText));
    }

    [AvaloniaFact]
    public async Task Cloud_saves_off_show_nothing()
    {
        using var harness = await OpenWithAsync(new CloudSaveInfo(CloudSaveState.Off, CloudStore.Steam));

        Assert.DoesNotContain(harness.All<Border>(), border => border.Name == "CloudSavesCallout" && border.IsEffectivelyVisible);
        Assert.False(Question(harness, "Does my store sync saves?")?.IsEffectivelyVisible ?? false);
        Assert.False(Shows(harness, text => text == _unknownText || text.StartsWith(_steamCloudStart, StringComparison.Ordinal)));
    }

    [AvaloniaFact]
    public async Task The_rules_explanations_sit_behind_a_question_button_with_one_line_always_shown()
    {
        using var harness = await EditorViewHarness.StartAsync();
        harness.Edit();
        const string savesRule = "Don't add a rule that matches your saves, or profiles stop switching them.";

        Assert.True(Shows(harness, text => text == savesRule));
        Assert.False(Shows(harness, text => text.StartsWith("SavePoint never copies or replaces", StringComparison.Ordinal)));
        Click(harness, Question(harness, "How do these rules work?"));

        Assert.True(Shows(harness, text => text.StartsWith("SavePoint never copies or replaces", StringComparison.Ordinal)));
        Assert.True(Shows(harness, text => text == savesRule));
    }

    [AvaloniaFact]
    public async Task The_link_under_the_rules_answer_opens_the_page_at_Files_to_leave_out_and_back_gives_the_editor_back()
    {
        using var harness = await EditorViewHarness.StartAsync();
        var editor = harness.Edit();
        editor.Name = "Half typed";
        Click(harness, Question(harness, "How do these rules work?"));
        var more = harness.All<Button>().FirstOrDefault(button => button.Name == "MoreButton" && button.IsEffectivelyVisible);
        Assert.NotNull(more);
        more.BringIntoView();
        harness.Settle();
        var offset = harness.Find<ScrollViewer>("FormScroll").Offset.Y;
        Assert.True(offset > 0, "The form did not need scrolling to reach the link.");

        more.Command!.Execute(null);
        harness.Settle();

        var page = Assert.IsType<Savepoint.App.Features.Help.HowItWorksViewModel>(harness.Shell.Content);
        Assert.Equal(Savepoint.App.Features.Help.HowItWorksSection.FilesToLeaveOut, page.Section);
        page.BackCommand.Execute(null);
        harness.Settle();
        Assert.Same(editor, harness.Shell.Content);
        Assert.Equal("Half typed", editor.Name);
        Assert.Equal(offset, harness.Find<ScrollViewer>("FormScroll").Offset.Y, 1);
        var focused = Assert.IsType<ToggleButton>(harness.Window.FocusManager!.GetFocusedElement());
        Assert.Equal("How do these rules work?", AutomationProperties.GetName(focused));
    }

    [AvaloniaFact]
    public async Task Back_from_the_page_opened_by_a_cloud_saves_link_puts_focus_on_that_question_and_not_on_the_name_field()
    {
        using var harness = await OpenWithAsync(new CloudSaveInfo(CloudSaveState.Unknown, CloudStore.None));
        Click(harness, Question(harness, "Does my store sync saves?"));
        var more = harness.All<Button>().FirstOrDefault(button => button.Name == "MoreButton" && button.IsEffectivelyVisible);
        Assert.NotNull(more);

        more.Command!.Execute(null);
        harness.Settle();
        Assert.IsType<Savepoint.App.Features.Help.HowItWorksViewModel>(harness.Shell.Content).BackCommand.Execute(null);
        harness.Settle();

        var focused = Assert.IsType<ToggleButton>(harness.Window.FocusManager!.GetFocusedElement());
        Assert.Equal("Does my store sync saves?", AutomationProperties.GetName(focused));
    }

    [AvaloniaFact]
    public async Task A_freshly_opened_editor_still_puts_focus_on_the_name_field()
    {
        using var harness = await EditorViewHarness.StartAsync();
        harness.Edit();
        harness.Settle();

        Assert.Same(harness.Find<TextBox>("NameField"), harness.Window.FocusManager!.GetFocusedElement());
    }

    [AvaloniaFact]
    public async Task The_stop_detection_mod_file_and_app_id_helpers_sit_behind_their_own_question_buttons()
    {
        using var harness = await EditorViewHarness.StartAsync();
        harness.Edit();
        var buttons = harness.All<ToggleButton>().Where(button => AutomationProperties.GetName(button) is "When do I need the program name?" or "When do I list mod files?" or "When do I need the Steam app ID?").ToList();

        Assert.Equal(3, buttons.Count);
        Assert.False(Shows(harness, text => text == "Used when the launcher closes itself before the game opens."));
        Assert.False(Shows(harness, text => text.StartsWith("Fill this in when your game keeps mods", StringComparison.Ordinal)));
        Assert.False(Shows(harness, text => text.StartsWith("SavePoint uses it to find the game's cover", StringComparison.Ordinal)));

        foreach (var button in buttons)
        {
            Click(harness, button);
        }

        Assert.True(Shows(harness, text => text == "Used when the launcher closes itself before the game opens."));
        Assert.True(Shows(harness, text => text.StartsWith("Fill this in when your game keeps mods", StringComparison.Ordinal)));
        Assert.True(Shows(harness, text => text.StartsWith("SavePoint uses it to find the game's cover", StringComparison.Ordinal)));
    }

    // Explanations of an idea the "How SavePoint works" page covers end with its link. A question whose idea is not on the page has nothing to link to.
    private static readonly string[] _questionsWithNoPageTopic = [];

    [AvaloniaFact]
    public async Task Every_question_button_in_the_editor_opens_an_answer_that_ends_with_the_page_link_unless_the_page_has_no_topic_for_it()
    {
        using var harness = await OpenWithAsync(new CloudSaveInfo(CloudSaveState.Unknown, CloudStore.None));
        var questions = harness.All<Savepoint.App.Features.Help.HelpQuestionView>().ToList();

        Assert.True(questions.Count >= 7, $"Only {questions.Count} question buttons were found.");
        foreach (var question in questions)
        {
            var help = Assert.IsType<Savepoint.App.Features.Help.ContextualHelpViewModel>(question.DataContext);
            help.IsOpen = true;
            harness.Settle();
            var answer = harness.All<Savepoint.App.Features.Help.HelpAnswerView>().Single(view => ReferenceEquals(view.DataContext, help));
            var more = answer.GetVisualDescendants().OfType<Button>().Single(button => button.Name == "MoreButton");

            Assert.Equal(!_questionsWithNoPageTopic.Contains(help.AutomationName), more.IsEffectivelyVisible);
            help.IsOpen = false;
        }
    }

    [AvaloniaFact]
    public async Task Back_from_the_page_opened_by_the_mods_link_puts_focus_on_that_question()
    {
        using var harness = await EditorViewHarness.StartAsync();
        harness.New();
        harness.Settle();
        Click(harness, Question(harness, "Does my game keep mods outside its folder?"));
        var more = harness.All<Button>().First(button => button.Name == "MoreButton" && button.IsEffectivelyVisible);

        more.Command!.Execute(null);
        harness.Settle();
        var page = Assert.IsType<Savepoint.App.Features.Help.HowItWorksViewModel>(harness.Shell.Content);
        Assert.Equal(Savepoint.App.Features.Help.HowItWorksSection.ModsFolders, page.Section);
        page.BackCommand.Execute(null);
        harness.Settle();

        var focused = Assert.IsType<ToggleButton>(harness.Window.FocusManager!.GetFocusedElement());
        Assert.Equal("Does my game keep mods outside its folder?", AutomationProperties.GetName(focused));
    }

    [AvaloniaFact]
    public async Task The_process_name_nudge_shows_under_how_to_start_while_More_settings_is_closed_and_its_button_fills_the_field()
    {
        var game = SampleScenario.Caretaker;
        var backend = new FakeBackend(ScenarioBuilder.Empty().WithGame(game).WithExecutables(game.Install.Path, "TheLastCaretaker.exe"));
        using var harness = await EditorViewHarness.StartAsync(backend, height: 1400);
        var editor = harness.Edit(game.Id);
        var toggle = Question(harness, "More settings");
        Assert.NotNull(toggle);
        Assert.False(toggle.IsChecked);

        editor.LaunchChoice = 1;
        harness.Settle();

        var row = harness.Find<StackPanel>("ProcessWhyRow");
        Assert.True(row.IsEffectivelyVisible);
        Assert.True(row.TranslatePoint(new Point(0, 0), harness.View)!.Value.Y < toggle.TranslatePoint(new Point(0, 0), harness.View)!.Value.Y);
        var button = harness.Find<Button>("UseSuggestedProcessButton");
        Assert.Equal("Use TheLastCaretaker.exe", button.Content);

        button.Command!.Execute(null);
        harness.Settle();

        Assert.Equal("TheLastCaretaker.exe", editor.ProcessName);
        Assert.Equal("TheLastCaretaker.exe", harness.Find<TextBox>("ProcessField").Text);
        Assert.False(row.IsEffectivelyVisible);
        Assert.False(toggle.IsChecked);
    }

    [AvaloniaFact]
    public async Task No_two_question_buttons_in_the_editor_show_the_same_text()
    {
        using var harness = await EditorViewHarness.StartAsync();
        harness.Edit();

        var questions = harness.All<Savepoint.App.Features.Help.HelpQuestionView>().Select(view => Assert.IsType<Savepoint.App.Features.Help.ContextualHelpViewModel>(view.DataContext).Question).ToList();

        Assert.Contains("When do I list mod files?", questions);
        Assert.Equal(questions.Count, questions.Distinct().Count());
    }

    [AvaloniaFact]
    public async Task The_Steam_app_ID_label_does_not_say_optional_because_everything_in_More_settings_is()
    {
        using var harness = await EditorViewHarness.StartAsync();
        harness.Edit();

        Assert.Equal("Steam app ID", AutomationProperties.GetName(harness.Find<TextBox>("SteamAppIdField")));
    }

    // Each question button sits under its field with its icon on the field's left edge, not indented by the button's own padding.
    [AvaloniaTheory]
    [InlineData("ModsQuestion")]
    [InlineData("ModFoldersQuestion")]
    [InlineData("AntiCheatQuestion")]
    [InlineData("RulesQuestion")]
    [InlineData("ProcessQuestion")]
    [InlineData("SteamAppIdQuestion")]
    public async Task A_question_button_starts_at_the_left_edge_of_the_fields_above_it(string name)
    {
        using var harness = await EditorViewHarness.StartAsync();
        var editor = harness.Edit();
        editor.IsMoreSettingsOpen = true;
        harness.Settle();

        var question = harness.Find<Savepoint.App.Features.Help.HelpQuestionView>(name);
        var icon = question.GetVisualDescendants().OfType<TextBlock>().First(block => block.Classes.Contains("icon"));
        var group = Assert.IsAssignableFrom<Control>(question.GetVisualParent());

        var iconLeft = icon.TranslatePoint(new Point(0, 0), group)!.Value.X;

        Assert.InRange(iconLeft, -0.5, 0.5);
    }

    [AvaloniaFact]
    public async Task More_settings_is_closed_on_Add_a_game_and_opens_with_its_toggle()
    {
        using var harness = await EditorViewHarness.StartAsync();
        harness.New();
        var toggle = Question(harness, "More settings");
        Assert.NotNull(toggle);
        Assert.False(harness.Find<TextBox>("ProcessField").IsEffectivelyVisible);
        Assert.False(harness.Find<ContentControl>("IgnoreRulesHost").IsEffectivelyVisible);
        Assert.False(harness.Find<ContentControl>("InstallIgnoreRulesHost").IsEffectivelyVisible);
        Assert.False(harness.Find<ContentControl>("LaunchArgsHost").IsEffectivelyVisible);
        Assert.False(harness.Find<TextBox>("SteamAppIdField").IsEffectivelyVisible);
        Assert.True(harness.Find<ContentControl>("ModFoldersHost").IsEffectivelyVisible);

        Click(harness, toggle);

        Assert.True(harness.Find<TextBox>("ProcessField").IsEffectivelyVisible);
        Assert.True(harness.Find<ContentControl>("IgnoreRulesHost").IsEffectivelyVisible);
        Assert.True(harness.Find<ContentControl>("InstallIgnoreRulesHost").IsEffectivelyVisible);
        Assert.True(harness.Find<ContentControl>("LaunchArgsHost").IsEffectivelyVisible);
        Assert.True(harness.Find<TextBox>("SteamAppIdField").IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public async Task More_settings_is_open_when_editing_a_game_that_has_those_settings()
    {
        using var harness = await EditorViewHarness.StartAsync();
        harness.Edit();

        Assert.True(Question(harness, "More settings")?.IsChecked);
        Assert.True(harness.Find<ContentControl>("IgnoreRulesHost").IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public async Task A_refused_Steam_app_id_opens_More_settings_and_shows_the_problem()
    {
        using var harness = await EditorViewHarness.StartAsync();
        var editor = harness.New();
        Assert.False(harness.Find<TextBox>("SteamAppIdField").IsEffectivelyVisible);

        editor.SteamAppIdText = "abc";
        harness.Settle();

        Assert.True(harness.Find<TextBox>("SteamAppIdField").IsEffectivelyVisible);
        Assert.True(harness.Find<TextBlock>("SteamAppIdErrorText").IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public async Task More_settings_comes_after_the_core_fields_in_reading_order()
    {
        using var harness = await EditorViewHarness.StartAsync(height: 1400);
        harness.New();
        var toggle = Question(harness, "More settings");
        Assert.NotNull(toggle);
        Click(harness, toggle);

        double Top(Control control) => control.TranslatePoint(new Point(0, 0), harness.View)!.Value.Y;
        var toggleTop = Top(toggle);

        foreach (var core in new[] { "NameField", "InstallField", "SavesField", "ExeField" })
        {
            Assert.True(Top(harness.Find<TextBox>(core)) < toggleTop, core);
        }

        Assert.True(Top(harness.Find<TextBox>("ProcessField")) > toggleTop);
        Assert.True(Top(harness.Find<ContentControl>("ModFoldersHost")) < toggleTop);
    }

    [AvaloniaFact]
    public async Task A_template_game_shows_its_notes_as_an_info_line()
    {
        using var harness = await EditorViewHarness.StartAsync();
        harness.New(SampleScenario.Caretaker with { Notes = "Saves are in Steam's userdata folder; add the saves folder by hand." });

        Assert.True(Shows(harness, text => text == "This game's saves are in Steam's userdata folder. Choose the saves folder yourself."));
    }

    [AvaloniaFact]
    public async Task A_game_without_template_notes_shows_no_info_line()
    {
        using var harness = await EditorViewHarness.StartAsync();
        harness.New();

        var line = harness.All<Control>().FirstOrDefault(control => control.Name == "TemplateNotesLine");
        Assert.NotNull(line);
        Assert.False(line.IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public async Task Tab_moves_from_the_start_choice_through_the_saves_and_mods_fields_before_More_settings()
    {
        using var harness = await EditorViewHarness.StartAsync(height: 1400);
        harness.New();
        var toggle = Question(harness, "More settings");
        Assert.NotNull(toggle);

        var visited = new List<string>();
        harness.Find<TextBox>("ExeField").Focus();
        harness.Settle();
        for (var i = 0; i < 40 && !toggle.IsFocused; i++)
        {
            harness.Window.KeyPress(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
            harness.Window.KeyRelease(Key.Tab, RawInputModifiers.None, PhysicalKey.Tab, null);
            harness.Settle();
            if (harness.Window.FocusManager!.GetFocusedElement() is TextBox { Name: { } name })
            {
                visited.Add(name);
            }
        }

        Assert.True(toggle.IsFocused);
        Assert.Equal(["SavesField", "ModsField"], visited);
    }
}
