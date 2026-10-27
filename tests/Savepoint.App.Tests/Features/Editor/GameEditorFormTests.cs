using Savepoint.App.Tests.Fakes;
using Savepoint.Core.Catalog;

namespace Savepoint.App.Tests.Features.Editor;

public class GameEditorFormTests
{
    [Fact]
    public void A_new_game_starts_empty_with_the_create_title_and_Save_unavailable()
    {
        var editor = EditorRig.Create().New();

        Assert.Equal("Add a game", editor.Title);
        Assert.True(editor.IsCreating);
        Assert.False(editor.IsEditing);
        Assert.Equal(string.Empty, editor.Name);
        Assert.Equal(string.Empty, editor.InstallPath);
        Assert.Equal(string.Empty, editor.SavesPath);
        Assert.Equal(0, editor.LaunchChoice);
        Assert.Empty(editor.IgnoreRules.Values);
        Assert.Equal(["**/*.log", "**/*.dmp", "**/*.mdmp"], editor.InstallIgnoreRules.Values);
        Assert.False(editor.CanSave);
    }

    [Fact]
    public void A_new_game_says_in_plain_words_why_logs_and_crash_reports_are_left_out_of_its_own_folder()
    {
        var editor = EditorRig.Create().New();

        Assert.True(editor.HasDefaultRulesNote);
        Assert.Equal("New games leave out logs and crash reports, which a game rewrites while it runs and SavePoint would take for a game update. Remove a rule to keep them in your snapshots.", editor.DefaultRulesNote);
    }

    [Fact]
    public void Both_lists_say_which_folders_they_reach()
    {
        var editor = EditorRig.Create().New();

        Assert.Equal("Files to leave out of the game's own folder", editor.InstallIgnoreRules.GroupName);
        Assert.Equal("Files to leave out of every folder", editor.IgnoreRules.GroupName);
    }

    [Fact]
    public void Each_list_has_its_own_add_name_help_text_and_remove_names()
    {
        var editor = EditorRig.Create().New();

        Assert.Equal("Add a rule for the game's own folder", editor.InstallIgnoreRules.AddAutomationName);
        Assert.Equal("Add a rule for every folder", editor.IgnoreRules.AddAutomationName);
        Assert.Equal("SavePoint never copies or replaces matching files, such as logs and crash reports. These rules never apply to your saves folder. Use * for any name and ** for any folder.", editor.InstallIgnoreRules.HelpText);
        Assert.Equal("SavePoint never copies or replaces files that match. Use * for any name and ** for any folder. Don't add a rule that matches your saves, or profiles stop switching them.", editor.IgnoreRules.HelpText);
        Assert.Equal("Remove **/*.log from Files to leave out of the game's own folder", editor.InstallIgnoreRules.Items[0].RemoveLabel);
        Assert.NotEqual(editor.LaunchArgs.AddAutomationName, editor.ModFolders.AddAutomationName);
    }

    [Fact]
    public void Removing_a_default_install_rule_makes_the_form_dirty()
    {
        var editor = EditorRig.Create().New();
        Assert.False(editor.IsDirty);

        editor.InstallIgnoreRules.Items[0].RemoveCommand.Execute(null);

        Assert.True(editor.IsDirty);
    }

    [Fact]
    public void An_existing_game_keeps_its_rules_as_they_are_and_has_no_note()
    {
        var bare = SampleScenario.Caretaker with { IgnorePaths = [] };
        var rig = EditorRig.Create(new FakeBackend(ScenarioBuilder.Empty().WithGame(bare)));

        var editor = rig.Edit(bare.Id);

        Assert.Empty(editor.IgnoreRules.Values);
        Assert.False(editor.HasDefaultRulesNote);
        Assert.False(editor.IsDirty);
    }

    [Fact]
    public void A_definition_file_is_not_given_rules_it_does_not_have()
    {
        var rig = EditorRig.Create();

        var editor = rig.New(SampleScenario.Caretaker with { Id = "imported", IgnorePaths = ["Saves/*.tmp"] });

        Assert.Equal(["Saves/*.tmp"], editor.IgnoreRules.Values);
        Assert.False(editor.HasDefaultRulesNote);
    }

    [Fact]
    public void A_prefilled_form_has_no_note_even_when_its_rules_are_the_defaults()
    {
        var rig = EditorRig.Create();

        var editor = rig.New(rig.Backend.Games.Templates[0].ToDraft());

        Assert.Equal(RunTimeOutput.DefaultRules.Order(), editor.InstallIgnoreRules.Values.Concat(editor.IgnoreRules.Values).Where(RunTimeOutput.DefaultRules.Contains).Distinct().Order());
        Assert.False(editor.HasDefaultRulesNote);
    }

    [Fact]
    public void A_definition_file_that_happens_to_hold_the_default_rules_gets_no_note()
    {
        var rig = EditorRig.Create();

        var editor = rig.New(SampleScenario.Caretaker with { Id = "imported", IgnorePaths = RunTimeOutput.DefaultRules });

        Assert.False(editor.HasDefaultRulesNote);
    }

    [Fact]
    public void An_existing_game_fills_every_field_and_the_title_names_it()
    {
        var editor = EditorRig.Create().Edit();

        Assert.Equal("Edit Skyrim Special Edition", editor.Title);
        Assert.True(editor.IsEditing);
        Assert.False(editor.IsCreating);
        Assert.Equal("Skyrim Special Edition", editor.Name);
        Assert.Equal(SampleScenario.SkyrimInstall, editor.InstallPath);
        Assert.Equal(SampleScenario.SkyrimSavesRaw, editor.SavesPath);
        Assert.Equal(0, editor.LaunchChoice);
        Assert.Equal("SkyrimSELauncher.exe", editor.ExePath);
        Assert.Equal("SkyrimSE.exe", editor.ProcessName);
        Assert.Equal(["**/*.log", "SKSE/Plugins/*.log", "**/Crash Logs/**"], editor.IgnoreRules.Values);
        Assert.Equal(["Data", "plugins.txt"], editor.ModFolders.Values);
        Assert.Empty(editor.LaunchArgs.Values);
        Assert.True(editor.CanSave);
    }

    [Fact]
    public void A_game_started_through_Steam_opens_on_the_Steam_choice_with_its_link()
    {
        var steam = SampleScenario.Caretaker with { Launch = new LaunchConfig(LaunchMethod.Uri, Uri: "steam://run/1105450", Args: ["-windowed"]) };
        var rig = EditorRig.Create(new FakeBackend(ScenarioBuilder.Empty().WithGame(steam)));

        var editor = rig.Edit(steam.Id);

        Assert.Equal(1, editor.LaunchChoice);
        Assert.Equal("steam://run/1105450", editor.Uri);
        Assert.Equal(["-windowed"], editor.LaunchArgs.Values);
    }

    [Fact]
    public void A_template_or_imported_definition_prefills_a_new_game_without_saving_anything()
    {
        var rig = EditorRig.Create();
        var template = rig.Backend.Games.Templates[0].ToDraft();

        var editor = rig.New(template);

        Assert.True(editor.IsCreating);
        Assert.Equal(template.Name, editor.Name);
        Assert.Equal(template.Install.Path, editor.InstallPath);
        Assert.Equal(template.External?.Path ?? string.Empty, editor.SavesPath);
        Assert.Equal(template.Launch.ExePath, editor.ExePath);
        Assert.Equal(template.IgnorePaths, editor.IgnoreRules.Values);
        Assert.Empty(rig.Calls("AddAsync"));
    }

    [Theory]
    [InlineData("", "C:\\Games\\X", false)]
    [InlineData("   ", "C:\\Games\\X", false)]
    [InlineData("X", "", false)]
    [InlineData("X", "   ", false)]
    [InlineData("X", "C:\\Games\\X", true)]
    public void Save_needs_a_name_and_an_install_folder_that_are_not_blank(string name, string install, bool expected)
    {
        var editor = EditorRig.Create().New();

        editor.Name = name;
        editor.InstallPath = install;

        Assert.Equal(expected, editor.CanSave);
        Assert.Equal(expected, editor.SaveCommand.CanExecute(null));
    }

    [Fact]
    public void Editing_a_field_clears_the_error_shown_on_it()
    {
        var editor = EditorRig.Create().New();
        editor.NameError = "Give the game a name.";
        editor.InstallError = "Choose the folder where the game is installed.";

        editor.Name = "X";

        Assert.Null(editor.NameError);
        Assert.NotNull(editor.InstallError);

        editor.InstallPath = "C:\\Games\\X";

        Assert.Null(editor.InstallError);
    }

    [Fact]
    public void The_form_is_clean_until_something_changes_and_clean_again_when_the_change_is_undone()
    {
        var editor = EditorRig.Create().Edit();
        Assert.False(editor.IsDirty);

        editor.Name = "Skyrim";
        Assert.True(editor.IsDirty);

        editor.Name = "Skyrim Special Edition";
        Assert.False(editor.IsDirty);

        editor.IgnoreRules.Items[0].RemoveCommand.Execute(null);
        Assert.True(editor.IsDirty);
    }

    [Fact]
    public void An_empty_new_form_is_clean_and_a_prefilled_one_is_clean_until_edited()
    {
        var rig = EditorRig.Create();
        Assert.False(rig.New().IsDirty);

        var prefilled = rig.New(rig.Backend.Games.Templates[0].ToDraft());
        Assert.False(prefilled.IsDirty);

        prefilled.InstallPath = "D:\\Elsewhere";
        Assert.True(prefilled.IsDirty);
    }
}
