using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Savepoint.App.Features.Common;
using Savepoint.App.Features.Common.Errors;
using Savepoint.App.Features.Editor;
using Savepoint.App.Tests.Fakes;
using Savepoint.App.Tests.Features.Shell;
using Savepoint.App.Tests.Theme;
using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;
using Savepoint.Core.Platform;

namespace Savepoint.App.Tests.Features.Editor;

public class GameEditorViewTests
{
    private static void Press(EditorViewHarness harness, Key key, PhysicalKey physical)
    {
        harness.Window.KeyPress(key, RawInputModifiers.None, physical, null);
        harness.Window.KeyRelease(key, RawInputModifiers.None, physical, null);
        harness.Settle();
    }

    private static void FocusAndSettle(EditorViewHarness harness, Control control)
    {
        control.Focus();
        harness.Settle();
    }

    private static string? FocusedName(EditorViewHarness harness) =>
        harness.Window.FocusManager!.GetFocusedElement() is Control focused ? AutomationProperties.GetName(focused) : null;

    private static T InHost<T>(EditorViewHarness harness, string host, string name)
        where T : Control => ShellViewHarness.FindIn<T>(harness.Find<ContentControl>(host), name);

    [Fact]
    public void Every_editor_view_is_found_by_its_view_model()
    {
        Assert.Equal(typeof(GameEditorView), ViewLocator.ViewTypeFor(typeof(GameEditorViewModel)));
        Assert.Equal(typeof(ChipListView), ViewLocator.ViewTypeFor(typeof(ChipListViewModel)));
        Assert.Equal(typeof(ConsentDialogView), ViewLocator.ViewTypeFor(typeof(ConsentDialogViewModel)));
        Assert.Equal(typeof(ReconnectDialogView), ViewLocator.ViewTypeFor(typeof(ReconnectDialogViewModel)));
    }

    [AvaloniaFact]
    public async Task The_pane_shows_the_title_and_Export_while_editing_and_Import_while_adding()
    {
        using var harness = await EditorViewHarness.StartAsync();

        harness.Edit();
        Assert.Equal("Edit Skyrim Special Edition", harness.Find<TextBlock>("TitleText").Text);
        Assert.True(harness.Find<Button>("ExportButton").IsEffectivelyVisible);
        Assert.False(harness.Find<Button>("ImportButton").IsEffectivelyVisible);

        harness.New();
        Assert.Equal("Add a game", harness.Find<TextBlock>("TitleText").Text);
        Assert.False(harness.Find<Button>("ExportButton").IsEffectivelyVisible);
        Assert.True(harness.Find<Button>("ImportButton").IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public async Task The_name_field_has_the_focus_when_the_pane_opens()
    {
        using var harness = await EditorViewHarness.StartAsync();

        harness.New();
        Assert.True(harness.Find<TextBox>("NameField").IsFocused);

        harness.Edit();
        Assert.True(harness.Find<TextBox>("NameField").IsFocused);
    }

    [AvaloniaFact]
    public async Task Save_is_disabled_until_there_is_a_name_and_an_install_folder()
    {
        using var harness = await EditorViewHarness.StartAsync();
        var editor = harness.New();
        var save = harness.Find<Button>("SaveButton");
        Assert.False(save.IsEffectivelyEnabled);

        harness.Find<TextBox>("NameField").Text = "Hollow Knight";
        harness.Find<TextBox>("InstallField").Text = @"C:\Games\Hollow Knight";
        harness.Settle();

        Assert.Equal("Hollow Knight", editor.Name);
        Assert.True(save.IsEffectivelyEnabled);
    }

    [AvaloniaFact]
    public async Task The_fields_show_the_game_and_write_back_to_it()
    {
        using var harness = await EditorViewHarness.StartAsync();
        var editor = harness.Edit();

        Assert.Equal("Skyrim Special Edition", harness.Find<TextBox>("NameField").Text);
        Assert.Equal(SampleScenario.SkyrimInstall, harness.Find<TextBox>("InstallField").Text);
        Assert.Equal(SampleScenario.SkyrimSavesRaw, harness.Find<TextBox>("SavesField").Text);
        Assert.Equal("SkyrimSELauncher.exe", harness.Find<TextBox>("ExeField").Text);
        Assert.Equal("SkyrimSE.exe", harness.Find<TextBox>("ProcessField").Text);

        harness.Find<TextBox>("ExeField").Text = "other.exe";
        harness.Find<TextBox>("ProcessField").Text = "other";
        harness.Settle();

        Assert.Equal("other.exe", editor.ExePath);
        Assert.Equal("other", editor.ProcessName);
    }

    [AvaloniaFact]
    public async Task The_start_choice_switches_between_the_executable_and_the_Steam_link()
    {
        using var harness = await EditorViewHarness.StartAsync();
        var editor = harness.Edit();
        var panel = harness.Find<StackPanel>("ExePanel");
        var uri = harness.Find<StackPanel>("UriPanel");
        Assert.True(panel.IsEffectivelyVisible);
        Assert.False(uri.IsEffectivelyVisible);

        harness.Find<ListBox>("LaunchChoiceList").SelectedIndex = 1;
        harness.Settle();

        Assert.Equal(1, editor.LaunchChoice);
        Assert.False(panel.IsEffectivelyVisible);
        Assert.True(uri.IsEffectivelyVisible);
        Assert.True(harness.Find<Border>("SteamWarningCallout").IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public async Task The_warning_and_the_cloud_save_guidance_are_always_shown()
    {
        using var harness = await EditorViewHarness.StartAsync();
        harness.Edit();

        Assert.True(harness.Find<Border>("SteamWarningCallout").IsEffectivelyVisible);
        Assert.True(harness.Find<Border>("GuidanceStrip").IsEffectivelyVisible);
        var texts = harness.All<TextBlock>().Select(block => block.Text).ToList();
        Assert.Contains(texts, text => text is not null && text.StartsWith("Turn off cloud saves for this game"));
        Assert.Contains("Don't use SavePoint with online games protected by anti-cheat. Changed game files can get your account flagged, and those games can't be modded or rolled back anyway.", texts);
    }

    [AvaloniaFact]
    public async Task The_run_choice_and_the_warning_name_the_operating_systems_kind_of_program()
    {
        using var harness = await EditorViewHarness.StartAsync(os: "macos");
        harness.Edit();

        var texts = harness.All<TextBlock>().Select(block => block.Text).ToList();
        Assert.Contains("Run the app", texts);
        Assert.Contains(texts, text => text is not null && text.EndsWith("Running the app avoids that."));
    }

    [AvaloniaFact]
    public async Task The_confirmed_line_and_the_resolved_line_show_for_an_edited_game_and_not_for_a_new_one()
    {
        using var harness = await EditorViewHarness.StartAsync();
        harness.Edit();

        Assert.True(harness.Find<StackPanel>("InstallConfirmedLine").IsEffectivelyVisible);
        Assert.True(harness.Find<StackPanel>("SavesConfirmedLine").IsEffectivelyVisible);
        Assert.True(harness.Find<Grid>("SavesResolvesLine").IsEffectivelyVisible);

        harness.New();

        Assert.False(harness.Find<StackPanel>("InstallConfirmedLine").IsEffectivelyVisible);
        Assert.False(harness.Find<Grid>("SavesResolvesLine").IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public async Task A_field_problem_shows_under_its_field_and_outlines_the_field_in_the_error_color()
    {
        using var harness = await EditorViewHarness.StartAsync();
        var editor = harness.New();
        editor.Name = "Hollow Knight";
        editor.InstallPath = @"C:\Games\Hollow Knight";
        await editor.SaveCommand.ExecuteAsync(null);
        harness.Settle();

        var text = harness.Find<TextBlock>("ExeErrorText");
        Assert.True(text.IsEffectivelyVisible);
        Assert.Equal("Choose the file that starts the game.", text.Text);
        Assert.Contains("error", harness.Find<TextBox>("ExeField").Classes);
        Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(text));
    }

    [AvaloniaFact]
    public async Task A_folder_problem_shows_the_error_card_under_the_folder_field()
    {
        using var harness = await EditorViewHarness.StartAsync();
        var editor = harness.New();
        editor.Name = "Skyrim";
        editor.InstallPath = @"C:\Program Files (x86)";
        editor.ExePath = "skyrim.exe";
        harness.Backend.FailNext<IGamesApi>("AddAsync", SampleScenario.FolderTooBroadError);
        var saving = editor.SaveCommand.ExecuteAsync(null);
        harness.Settle();
        Assert.IsType<ConsentDialogViewModel>(harness.Dialogs.Current).ConfirmCommand.Execute(null);
        await saving;
        harness.Settle();

        var host = harness.Find<ContentControl>("InstallCardHost");
        Assert.True(host.IsEffectivelyVisible);
        Assert.Single(host.GetVisualDescendants().OfType<ErrorCardView>());
        Assert.False(harness.Find<ContentControl>("SavesCardHost").IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public async Task A_failure_that_belongs_to_no_field_shows_one_card_above_the_form()
    {
        using var harness = await EditorViewHarness.StartAsync();
        var editor = harness.Edit();
        editor.Name = "Skyrim SE";
        harness.Backend.FailNext<IGamesApi>("UpdateAsync", new ErrorInfo("error.unknown", new Dictionary<string, object?>(), "boom"));

        await editor.SaveCommand.ExecuteAsync(null);
        harness.Settle();

        var host = harness.Find<ContentControl>("FormErrorHost");
        Assert.True(host.IsEffectivelyVisible);
        Assert.Single(host.GetVisualDescendants().OfType<ErrorCardView>());
    }

    [AvaloniaFact]
    public async Task Suggestions_appear_as_buttons_that_fill_the_field()
    {
        var backend = new FakeBackend(ScenarioBuilder.Sample().WithExecutables(SampleScenario.SkyrimInstall, "SkyrimSELauncher.exe", "SkyrimSE.exe"));
        using var harness = await EditorViewHarness.StartAsync(backend);
        var editor = harness.Edit();

        var row = harness.Find<StackPanel>("ExeSuggestionRow");
        Assert.True(row.IsEffectivelyVisible);
        var button = Assert.Single(row.GetVisualDescendants().OfType<Button>());
        Assert.Equal("SkyrimSE.exe", button.Content);

        button.Command!.Execute(button.CommandParameter);
        harness.Settle();

        Assert.Equal("SkyrimSE.exe", editor.ExePath);
        Assert.Equal("SkyrimSE.exe", harness.Find<TextBox>("ExeField").Text);
    }

    [AvaloniaFact]
    public async Task A_link_start_explains_the_process_name_and_offers_the_games_program_as_a_button()
    {
        var backend = new FakeBackend(ScenarioBuilder.Sample().WithExecutables(SampleScenario.SkyrimInstall, "SkyrimSELauncher.exe", "SkyrimSE.exe"));
        using var harness = await EditorViewHarness.StartAsync(backend);
        var editor = harness.Edit();
        var row = harness.Find<StackPanel>("ProcessWhyRow");
        Assert.False(row.IsEffectivelyVisible);

        editor.ProcessName = string.Empty;
        editor.LaunchChoice = 1;
        harness.Settle();

        Assert.True(row.IsEffectivelyVisible);
        Assert.Contains("can't tell when you've stopped playing", harness.Find<TextBlock>("ProcessWhyText").Text);
        var button = harness.Find<Button>("UseSuggestedProcessButton");
        Assert.Equal("Use SkyrimSE.exe", button.Content);

        button.Command!.Execute(null);
        harness.Settle();

        Assert.Equal("SkyrimSE.exe", harness.Find<TextBox>("ProcessField").Text);
        Assert.False(row.IsEffectivelyVisible);
    }

    [AvaloniaFact]
    public async Task The_common_locations_are_buttons_that_prefix_the_saves_field()
    {
        using var harness = await EditorViewHarness.StartAsync();
        var editor = harness.New();
        var row = harness.Find<Grid>("LocationRow");

        var buttons = row.GetVisualDescendants().OfType<Button>().ToList();
        Assert.Equal(["Documents", "Saved Games", "AppData", "Local AppData"], buttons.Select(button => button.Content));
        Assert.Equal(["{documents}", "{savedGames}", "{appData}", "{localAppData}"], buttons.Select(button => ToolTip.GetTip(button)));

        buttons[2].Command!.Execute(null);
        harness.Settle();

        Assert.Equal("{appData}", editor.SavesPath);
        Assert.Equal("{appData}", harness.Find<TextBox>("SavesField").Text);
    }

    [AvaloniaFact]
    public async Task Leaving_the_install_field_looks_for_programs_in_the_folder()
    {
        using var harness = await EditorViewHarness.StartAsync();
        var editor = harness.Edit();
        harness.Backend.Calls.Clear();

        FocusAndSettle(harness, harness.Find<TextBox>("InstallField"));
        harness.Find<TextBox>("InstallField").Text = @"D:\Games\Other";
        harness.Settle();
        Assert.Empty(harness.Backend.Calls.To<IGamesApi>("ListExecutablesAsync"));

        FocusAndSettle(harness, harness.Find<TextBox>("NameField"));

        Assert.Equal(@"D:\Games\Other", Assert.Single(harness.Backend.Calls.To<IGamesApi>("ListExecutablesAsync")).Args[0]);
        Assert.Equal(@"D:\Games\Other", editor.InstallPath);
    }

    [AvaloniaFact]
    public async Task Every_control_that_takes_input_is_named_for_assistive_technology()
    {
        using var harness = await EditorViewHarness.StartAsync();
        harness.Edit();

        Assert.Empty(AutomationAudit.IconButtonsWithoutNames(harness.View));
        Assert.Equal("Back", AutomationProperties.GetName(harness.Find<Button>("BackButton")));
        foreach (var name in new[] { "NameField", "InstallField", "SavesField", "ExeField", "ProcessField" })
        {
            Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(harness.Find<TextBox>(name))), name);
        }

        foreach (var name in new[] { "BrowseInstallButton", "BrowseSavesButton", "BrowseExeButton" })
        {
            Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(harness.Find<Button>(name))), name);
        }

        Assert.Equal("How to start the game", AutomationProperties.GetName(harness.Find<ListBox>("LaunchChoiceList")));
        Assert.Equal("Files to leave out of both folders", AutomationProperties.GetName(harness.Find<ContentControl>("IgnoreRulesHost").GetVisualDescendants().OfType<ItemsControl>().First()));
        Assert.Equal("Files to leave out of the game's own folder", AutomationProperties.GetName(harness.Find<ContentControl>("InstallIgnoreRulesHost").GetVisualDescendants().OfType<ItemsControl>().First()));
        Assert.True(harness.All<Button>().Where(button => button.Classes.Contains("icon")).Any());
    }

    [AvaloniaFact]
    public async Task Files_to_leave_out_carries_its_warning_beside_the_rules()
    {
        using var harness = await EditorViewHarness.StartAsync();
        harness.Edit();

        var texts = harness.All<TextBlock>().Where(block => block.IsEffectivelyVisible).Select(block => block.Text).ToList();

        Assert.Contains("FILES TO LEAVE OUT OF BOTH FOLDERS", texts);
        Assert.Contains("SavePoint never copies or replaces files that match these rules, in the game's own folder or in your saves folder. Use * for any name and ** for any folder. Don't add a rule that matches your saves, or profiles stop switching them.", texts);
        Assert.Contains("FILES TO LEAVE OUT OF THE GAME'S OWN FOLDER", texts);
        Assert.Contains("SavePoint never copies or replaces files in the game's own folder that match these rules, such as logs and crash reports. They never reach your saves folder. Use * for any name and ** for any folder.", texts);
    }

    [AvaloniaFact]
    public async Task The_chip_lists_name_every_remove_button_after_its_chip()
    {
        using var harness = await EditorViewHarness.StartAsync();
        harness.Edit();

        var names = harness.Find<ContentControl>("IgnoreRulesHost").GetVisualDescendants().OfType<Button>()
            .Where(button => button.Classes.Contains("icon"))
            .Select(button => AutomationProperties.GetName(button))
            .ToList();

        Assert.Equal(
            [
                "Remove **/*.log from Files to leave out of both folders",
                "Remove SKSE/Plugins/*.log from Files to leave out of both folders",
                "Remove **/Crash Logs/** from Files to leave out of both folders",
            ],
            names);
    }

    [AvaloniaFact]
    public async Task Each_rule_list_names_its_add_button_and_field_and_carries_its_help_text()
    {
        using var harness = await EditorViewHarness.StartAsync();
        harness.Edit();

        foreach (var (host, name, help) in new[]
        {
            ("InstallIgnoreRulesHost", "Add a rule for the game's own folder", "never reach your saves folder"),
            ("IgnoreRulesHost", "Add a rule for both folders", "in the game's own folder or in your saves folder"),
        })
        {
            var addChip = InHost<Button>(harness, host, "AddChip");
            var field = InHost<ChipEntryBox>(harness, host, "AddField");
            Assert.Equal(name, AutomationProperties.GetName(addChip));
            Assert.Equal(name, AutomationProperties.GetName(field));
            Assert.Contains(help, AutomationProperties.GetHelpText(field), StringComparison.Ordinal);
            Assert.Contains(help, AutomationProperties.GetHelpText(harness.Find<ContentControl>(host).GetVisualDescendants().OfType<ItemsControl>().First()), StringComparison.Ordinal);
        }
    }

    [AvaloniaFact]
    public async Task Adding_a_rule_opens_a_focused_field_and_Enter_adds_the_value()
    {
        using var harness = await EditorViewHarness.StartAsync();
        var editor = harness.Edit();
        var addChip = InHost<Button>(harness, "IgnoreRulesHost", "AddChip");
        var field = InHost<ChipEntryBox>(harness, "IgnoreRulesHost", "AddField");
        Assert.True(addChip.IsEffectivelyVisible);
        Assert.False(field.IsEffectivelyVisible);

        addChip.Command!.Execute(null);
        harness.Settle();
        addChip = InHost<Button>(harness, "IgnoreRulesHost", "AddChip");
        field = InHost<ChipEntryBox>(harness, "IgnoreRulesHost", "AddField");

        Assert.False(addChip.IsEffectivelyVisible);
        Assert.True(field.IsEffectivelyVisible);
        Assert.True(field.IsFocused);

        harness.Window.KeyTextInput("**/*.tmp");
        Press(harness, Key.Enter, PhysicalKey.Enter);

        Assert.Equal(["**/*.log", "SKSE/Plugins/*.log", "**/Crash Logs/**", "**/*.tmp"], editor.IgnoreRules.Values);
        Assert.False(editor.IgnoreRules.IsAdding);
    }

    [AvaloniaFact]
    public async Task Escape_cancels_the_add_and_leaving_the_field_keeps_what_was_typed()
    {
        using var harness = await EditorViewHarness.StartAsync();
        var editor = harness.Edit();

        InHost<Button>(harness, "ModFoldersHost", "AddChip").Command!.Execute(null);
        harness.Settle();
        Assert.True(InHost<ChipEntryBox>(harness, "ModFoldersHost", "AddField").IsFocused);
        harness.Window.KeyTextInput("Scripts");
        Press(harness, Key.Escape, PhysicalKey.Escape);

        Assert.Equal(["Data", "plugins.txt"], editor.ModFolders.Values);
        Assert.False(editor.ModFolders.IsAdding);

        InHost<Button>(harness, "ModFoldersHost", "AddChip").Command!.Execute(null);
        harness.Settle();
        harness.Window.KeyTextInput("Skse");
        FocusAndSettle(harness, harness.Find<TextBox>("NameField"));

        Assert.Equal(["Data", "plugins.txt", "Skse"], editor.ModFolders.Values);
    }

    [AvaloniaFact]
    public async Task The_folder_confirmation_names_the_paths_and_its_buttons_answer_it()
    {
        using var harness = await EditorViewHarness.StartAsync();
        // Skyrim names a saves folder as well as the install folder, so two folders are confirmed.
        var editor = harness.New(harness.Backend.Games.Templates.Single(t => t.Id == "skyrim-se").ToDraft(OsKind.Windows));
        // Templates leave the install folder for the player to pick.
        editor.InstallPath = @"C:\Games\SkyrimSE";
        var saving = editor.SaveCommand.ExecuteAsync(null);
        harness.Settle();

        var dialog = Assert.IsType<ConsentDialogViewModel>(harness.Dialogs.Current);
        var view = harness.Window.GetVisualDescendants().OfType<ConsentDialogView>().Single();
        var texts = view.GetVisualDescendants().OfType<TextBlock>().Select(block => block.Text).ToList();
        Assert.Contains("Confirm these folders", texts);
        Assert.Contains(dialog.Folders[0].Path, texts);
        Assert.Contains("GAME INSTALL FOLDER", texts);

        ShellViewHarness.FindIn<Button>(view, "ConfirmButton").Command!.Execute(null);
        await saving;

        Assert.Single(harness.Backend.Calls.To<IGamesApi>("AddAsync"));
    }

    [AvaloniaFact]
    public async Task The_reconnect_question_lists_the_profiles_and_its_buttons_answer_it()
    {
        var history = new OrphanedHistory("old-game", [RepoRole.Install], [new RecoverableProfile("profile/a", "Modded run")]);
        var backend = new FakeBackend(ScenarioBuilder.Sample().WithOrphanedHistory(@"C:\Games\Hollow Knight", history));
        using var harness = await EditorViewHarness.StartAsync(backend);
        var editor = harness.New();
        editor.Name = "Hollow Knight";
        editor.InstallPath = @"C:\Games\Hollow Knight";
        editor.ExePath = "hk.exe";
        var saving = editor.SaveCommand.ExecuteAsync(null);
        harness.Settle();
        Assert.IsType<ConsentDialogViewModel>(harness.Dialogs.Current).ConfirmCommand.Execute(null);
        var question = await EditorRig.Eventually(() => harness.Dialogs.Current as ReconnectDialogViewModel);
        Dispatcher.UIThread.RunJobs();

        var view = harness.Window.GetVisualDescendants().OfType<ReconnectDialogView>().Single();
        Assert.Contains("Recovered: Modded run", view.GetVisualDescendants().OfType<TextBlock>().Select(block => block.Text));

        ShellViewHarness.FindIn<Button>(view, "StartFreshButton").Command!.Execute(null);
        await saving;

        Assert.Equal(ReconnectChoice.StartFresh, await question.Result);
        Assert.Single(harness.Backend.Calls.To<IGamesApi>("ResolveOrphanAsync"));
    }

    [AvaloniaFact]
    public async Task A_field_in_error_carries_the_error_as_its_help_text_so_tabbing_back_to_it_repeats_the_problem()
    {
        using var harness = await EditorViewHarness.StartAsync(height: 1000);
        var editor = harness.Edit(SampleScenario.SkyrimId);

        editor.NameError = "Enter a name.";
        editor.InstallError = "Choose the install folder.";
        editor.SavesError = "Choose the saves folder.";
        editor.ExePathError = "Choose the file that starts the game.";
        editor.UriError = "Enter a Steam link.";
        editor.SteamAppIdError = "Enter a number.";
        harness.Settle();

        Assert.Equal("Enter a name.", AutomationProperties.GetHelpText(harness.Find<TextBox>("NameField")));
        Assert.Equal("Choose the install folder.", AutomationProperties.GetHelpText(harness.Find<TextBox>("InstallField")));
        Assert.Equal("Choose the saves folder.", AutomationProperties.GetHelpText(harness.Find<TextBox>("SavesField")));
        Assert.Equal("Choose the file that starts the game.", AutomationProperties.GetHelpText(harness.Find<TextBox>("ExeField")));
        Assert.Equal("Enter a Steam link.", AutomationProperties.GetHelpText(harness.Find<TextBox>("UriField")));
        Assert.Equal("Enter a number.", AutomationProperties.GetHelpText(harness.Find<TextBox>("SteamAppIdField")));

        editor.NameError = null;
        harness.Settle();

        Assert.True(string.IsNullOrEmpty(AutomationProperties.GetHelpText(harness.Find<TextBox>("NameField"))));
    }

    [AvaloniaFact]
    public async Task Removing_a_chip_from_the_keyboard_moves_focus_to_the_next_chip_and_after_the_last_to_the_add_chip()
    {
        using var harness = await EditorViewHarness.StartAsync(height: 1000);
        harness.Edit(SampleScenario.SkyrimId);
        var list = harness.All<ChipListView>().First(candidate => candidate.GetVisualDescendants().OfType<Button>().Count(button => button.Classes.Contains("icon")) >= 2);
        List<Button> Removers() => [.. list.GetVisualDescendants().OfType<Button>().Where(button => button.Classes.Contains("icon"))];
        var names = Removers().Select(button => AutomationProperties.GetName(button)).ToList();

        FocusAndSettle(harness, Removers()[0]);
        Press(harness, Key.Space, PhysicalKey.Space);
        await Task.Yield();
        harness.Settle();

        Assert.Equal(names[1], FocusedName(harness));

        while (Removers().Count > 1)
        {
            FocusAndSettle(harness, Removers()[^1]);
            Press(harness, Key.Space, PhysicalKey.Space);
            harness.Settle();
        }

        FocusAndSettle(harness, Removers()[0]);
        Press(harness, Key.Space, PhysicalKey.Space);
        harness.Settle();

        Assert.Equal("AddChip", (harness.Window.FocusManager!.GetFocusedElement() as Control)?.Name);
    }

    [AvaloniaFact]
    public async Task The_start_file_and_steam_link_fields_are_spoken_by_what_they_are_not_by_their_type()
    {
        using var harness = await EditorViewHarness.StartAsync();
        harness.Edit(SampleScenario.SkyrimId);

        Assert.Equal("File that starts the game", AutomationProperties.GetName(harness.Find<TextBox>("ExeField")));
        Assert.Equal("Steam launch link", AutomationProperties.GetName(harness.Find<TextBox>("UriField")));
    }

    [AvaloniaFact]
    public async Task The_steam_app_id_field_is_a_fixed_size_target_whatever_is_typed_in_it()
    {
        using var harness = await EditorViewHarness.StartAsync(height: 1000);
        harness.Edit(SampleScenario.SatisfactoryId);

        var field = harness.Find<TextBox>("SteamAppIdField");
        Assert.True(field.Bounds.Width >= 160, $"The empty field is {field.Bounds.Width:0}px wide.");

        field.Text = "abc";
        harness.Settle();

        Assert.True(field.Bounds.Width >= 160, $"The field holding abc is {field.Bounds.Width:0}px wide.");
    }
}
