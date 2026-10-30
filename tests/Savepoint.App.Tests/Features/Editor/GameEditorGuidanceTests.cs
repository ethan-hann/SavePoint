using Savepoint.App.Features.Editor;
using Savepoint.App.Tests.Fakes;
using Savepoint.Core.Api;
using Savepoint.Core.Errors;

namespace Savepoint.App.Tests.Features.Editor;

// The editor shows a warning or an explanation only when it applies: the store's cloud answer, the template's notes, and the settings
// most games never touch.
public class GameEditorGuidanceTests
{
    private const string _unknownAnswer = "If this game's store syncs cloud saves, it can bring back saves that SavePoint removed. Turn off cloud saves for this game in its store to be safe.";

    private static FakeGamesApi Games(EditorRig rig) => Assert.IsType<FakeGamesApi>(rig.Backend.Games);

    private static async Task<GameEditorViewModel> OpenWithAsync(CloudSaveInfo info)
    {
        var rig = EditorRig.Create();
        Games(rig).CloudSaves[SampleScenario.SkyrimId] = info;
        var editor = rig.Edit();
        await editor.LoadAsync();
        return editor;
    }

    [Theory]
    [InlineData(CloudStore.Steam)]
    [InlineData(CloudStore.Gog)]
    public async Task Cloud_saves_on_show_the_warning_and_no_question(CloudStore store)
    {
        var editor = await OpenWithAsync(new CloudSaveInfo(CloudSaveState.On, store));

        Assert.True(editor.HasCloudSavesNote);
        Assert.False(editor.HasCloudSavesUnknown);
    }

    [Theory]
    [InlineData(CloudStore.None)]
    [InlineData(CloudStore.Steam)]
    [InlineData(CloudStore.Gog)]
    public async Task Cloud_saves_unknown_show_the_question_and_no_warning(CloudStore store)
    {
        var editor = await OpenWithAsync(new CloudSaveInfo(CloudSaveState.Unknown, store));

        Assert.False(editor.HasCloudSavesNote);
        Assert.True(editor.HasCloudSavesUnknown);
        Assert.Equal("Does my store sync saves?", editor.CloudSavesHelp.Question);
        Assert.Equal(_unknownAnswer, editor.CloudSavesHelp.Answer);
        Assert.False(editor.CloudSavesHelp.IsOpen);
    }

    [Theory]
    [InlineData(CloudStore.Steam)]
    [InlineData(CloudStore.Gog)]
    public async Task Cloud_saves_off_show_nothing(CloudStore store)
    {
        var editor = await OpenWithAsync(new CloudSaveInfo(CloudSaveState.Off, store));

        Assert.False(editor.HasCloudSavesNote);
        Assert.False(editor.HasCloudSavesUnknown);
    }

    [Fact]
    public async Task A_failed_check_shows_the_question()
    {
        var rig = EditorRig.Create();
        rig.Backend.FailNext<IGamesApi>(nameof(IGamesApi.GetCloudSavesAsync), ErrorInfo.From(new GameNotFoundException(SampleScenario.SkyrimId)));
        var editor = rig.Edit();
        await editor.LoadAsync();

        Assert.False(editor.HasCloudSavesNote);
        Assert.True(editor.HasCloudSavesUnknown);
    }

    [Fact]
    public void A_game_that_is_not_saved_yet_shows_the_question_without_asking_the_store()
    {
        var rig = EditorRig.Create();

        var editor = rig.New();

        Assert.False(editor.HasCloudSavesNote);
        Assert.True(editor.HasCloudSavesUnknown);
        Assert.Empty(rig.Calls(nameof(IGamesApi.GetCloudSavesAsync)));
    }

    [Fact]
    public async Task An_open_cloud_answer_closes_when_the_store_gives_a_real_answer()
    {
        var rig = EditorRig.Create();
        Games(rig).CloudSaves[SampleScenario.SkyrimId] = new CloudSaveInfo(CloudSaveState.Unknown, CloudStore.None);
        var editor = rig.Edit();
        await editor.LoadAsync();
        editor.CloudCheckDelay = (_, _) => Task.CompletedTask;
        editor.CloudSavesHelp.IsOpen = true;

        Games(rig).CloudSavesForForm = (_, _) => Task.FromResult(new CloudSaveInfo(CloudSaveState.On, CloudStore.Steam));
        editor.InstallPath = @"D:\Games\Elsewhere";
        await EditorRig.Eventually(() => editor.HasCloudSavesNote ? editor.CloudSavesNote : null);

        Assert.False(editor.HasCloudSavesUnknown);
        Assert.False(editor.CloudSavesHelp.IsOpen);
    }

    [Fact]
    public void Every_explanation_is_a_question_button_with_its_own_answer()
    {
        var editor = EditorRig.Create().New();

        Assert.Equal("Can I use this with online games?", editor.AntiCheatHelp.Question);
        Assert.StartsWith("Don't use SavePoint with online games that have anti-cheat.", editor.AntiCheatHelp.Answer);
        Assert.Equal("How do these rules work?", editor.RulesHelp.Question);
        foreach (var help in new[] { editor.ProcessHelp, editor.ModFoldersHelp, editor.SteamAppIdHelp })
        {
            Assert.False(help.IsOpen);
        }

        Assert.Equal("Used when the launcher closes itself before the game opens.", editor.ProcessHelp.Answer);
        Assert.StartsWith("SavePoint uses it to find the game's cover", editor.SteamAppIdHelp.Answer);
        Assert.Contains("It doesn't change how the game starts.", editor.SteamAppIdHelp.Answer);
    }

    [Fact]
    public void The_rules_answer_names_the_whole_pattern_syntax()
    {
        var answer = EditorRig.Create().New().RulesHelp.Answer;

        Assert.Contains("* matches any name, ** any folders, ? one character and [abc] one of those characters", answer, StringComparison.Ordinal);
        Assert.Contains("starts with /", answer, StringComparison.Ordinal);
        Assert.Contains("ends with /", answer, StringComparison.Ordinal);
        Assert.Contains("starts with !", answer, StringComparison.Ordinal);
        Assert.Contains("last rule that matches wins", answer, StringComparison.Ordinal);
    }

    [Fact]
    public void Both_rule_lists_tell_a_screen_reader_that_they_hold_patterns_and_not_regular_expressions()
    {
        var editor = EditorRig.Create().New();

        Assert.Contains("not regular expressions", editor.IgnoreRules.HelpText, StringComparison.Ordinal);
        Assert.Contains("not regular expressions", editor.InstallIgnoreRules.HelpText, StringComparison.Ordinal);
    }

    [Fact]
    public void The_mods_folder_question_says_which_games_need_one_and_the_mod_file_answer_says_when_to_fill_it_in()
    {
        var editor = EditorRig.Create().New();

        Assert.Equal("Does my game keep mods outside its folder?", editor.ModsHelp.Question);
        Assert.StartsWith("Most games load mods from inside their own folder", editor.ModsHelp.Answer, StringComparison.Ordinal);
        Assert.Contains("7 Days to Die", editor.ModsHelp.Answer, StringComparison.Ordinal);
        Assert.Equal("When do I list mod files?", editor.ModFoldersHelp.Question);
        Assert.StartsWith("Fill this in when your game keeps mods inside its own folder", editor.ModFoldersHelp.Answer, StringComparison.Ordinal);
        Assert.Contains("never what SavePoint saves", editor.ModFoldersHelp.Answer, StringComparison.Ordinal);
        Assert.Contains("It only changes the mod file counts", editor.ModFoldersHelp.Answer, StringComparison.Ordinal);
        Assert.Contains("profile table and in the switch preview", editor.ModFoldersHelp.Answer, StringComparison.Ordinal);
    }

    [Fact]
    public void The_list_of_mod_files_is_named_for_the_inside_of_the_game_folder()
    {
        var editor = EditorRig.Create().New();

        Assert.Equal("Mod files inside the game folder", editor.ModFolders.GroupName);
    }

    [Theory]
    [InlineData("rules", "help:FilesToLeaveOut")]
    [InlineData("cloud", "help:CloudSaves")]
    [InlineData("process", "help:HowTheGameStarts")]
    [InlineData("modsFolder", "help:ModsFolders")]
    [InlineData("modFiles", "help:ModsFolders")]
    [InlineData("antiCheat", "help:OnlineGames")]
    [InlineData("steamAppId", "help:SteamAppId")]
    public void The_answers_that_have_a_page_topic_end_with_a_link_that_opens_the_page_at_it(string which, string expectedCall)
    {
        var rig = EditorRig.Create();
        var editor = rig.New();
        var help = which switch
        {
            "rules" => editor.RulesHelp,
            "cloud" => editor.CloudSavesHelp,
            "modsFolder" => editor.ModsHelp,
            "modFiles" => editor.ModFoldersHelp,
            "antiCheat" => editor.AntiCheatHelp,
            "steamAppId" => editor.SteamAppIdHelp,
            _ => editor.ProcessHelp,
        };
        help.IsOpen = true;

        Assert.True(help.HasLink);
        Assert.Equal("More in How SavePoint works", help.MoreLabel);
        help.OpenHowItWorksCommand.Execute(null);

        Assert.False(help.IsOpen);
        Assert.Equal([expectedCall], rig.Shell.Calls.Where(call => call.StartsWith("help", StringComparison.Ordinal)));
    }

    [Fact]
    public void The_stop_detection_mod_file_and_app_id_questions_each_name_their_field_and_read_the_same_to_a_screen_reader()
    {
        var editor = EditorRig.Create().New();

        Assert.Equal("When do I need the program name?", editor.ProcessHelp.Question);
        Assert.Equal("When do I list mod files?", editor.ModFoldersHelp.Question);
        Assert.Equal("When do I need the Steam app ID?", editor.SteamAppIdHelp.Question);
        Assert.All([editor.ProcessHelp, editor.ModFoldersHelp, editor.SteamAppIdHelp], help => Assert.Equal(help.Question, help.AutomationName));
        Assert.Equal("Can I use this with online games?", editor.AntiCheatHelp.AutomationName);
    }

    [Fact]
    public void More_settings_is_closed_on_Add_a_game()
    {
        var editor = EditorRig.Create().New();

        Assert.False(editor.IsMoreSettingsOpen);
    }

    [Fact]
    public void More_settings_is_open_when_the_edited_game_has_rules_set()
    {
        var editor = EditorRig.Create().Edit(SampleScenario.SkyrimId);

        Assert.True(editor.IsMoreSettingsOpen);
    }

    [Fact]
    public void More_settings_is_closed_when_the_edited_game_has_none_of_them_set()
    {
        var editor = EditorRig.Create().Edit(SampleScenario.CaretakerId);

        Assert.False(editor.IsMoreSettingsOpen);
    }

    public static TheoryData<string> SettingsInsideMoreSettings() => new()
    {
        "rule",
        "install rule",
        "process",
        "option",
        "app id",
    };

    // The mod files are in the Mods card, so a game that names them has nothing hidden in More settings.
    [Fact]
    public void More_settings_stays_closed_when_the_edited_game_only_names_mod_files()
    {
        var game = SampleScenario.Caretaker with { ModPathHints = ["Mods"] };
        var rig = EditorRig.Create(new FakeBackend(ScenarioBuilder.Empty().WithGame(game)));

        var editor = rig.Edit(game.Id);

        Assert.False(editor.IsMoreSettingsOpen);
    }

    [Theory]
    [MemberData(nameof(SettingsInsideMoreSettings))]
    public void More_settings_is_open_when_the_edited_game_has_any_one_of_them_set(string which)
    {
        var plain = SampleScenario.Caretaker;
        var game = which switch
        {
            "rule" => plain with { IgnorePaths = ["**/*.tmp"] },
            "install rule" => plain with { InstallIgnorePaths = ["**/*.log"] },
            "process" => plain with { Launch = plain.Launch with { ProcessNameForExitDetection = "Game.exe" } },
            "option" => plain with { Launch = plain.Launch with { Args = ["-skipintro"] } },
            _ => plain with { SteamAppId = 489830 },
        };
        var rig = EditorRig.Create(new FakeBackend(ScenarioBuilder.Empty().WithGame(game)));

        var editor = rig.Edit(game.Id);

        Assert.True(editor.IsMoreSettingsOpen);
    }

    [Fact]
    public void More_settings_opens_by_itself_when_the_Steam_app_id_is_refused()
    {
        var editor = EditorRig.Create().New();
        Assert.False(editor.IsMoreSettingsOpen);

        editor.SteamAppIdText = "abc";

        Assert.NotNull(editor.SteamAppIdError);
        Assert.True(editor.IsMoreSettingsOpen);
    }

    [Fact]
    public void A_game_added_from_a_template_shows_the_catalog_wording_of_the_templates_notes()
    {
        var editor = EditorRig.Create().New(SampleScenario.Caretaker with { Notes = "Saves are in Steam's userdata folder; add the saves folder by hand." });

        Assert.True(editor.HasTemplateNotes);
        Assert.Equal("This game's saves are in Steam's userdata folder. Choose the saves folder yourself.", editor.TemplateNotes);
    }

    [Fact]
    public void Two_known_sentences_in_one_note_show_as_two_catalog_sentences()
    {
        var notes = "We couldn't confirm where this game keeps its saves; add the saves folder by hand. Uses anti-cheat; don't use SavePoint with its online mode.";

        var editor = EditorRig.Create().New(SampleScenario.Caretaker with { Notes = notes });

        Assert.Equal(
            "SavePoint couldn't confirm where this game keeps its saves. Choose the saves folder yourself. This game uses anti-cheat. Don't use SavePoint with its online mode.",
            editor.TemplateNotes);
    }

    [Fact]
    public void A_sentence_the_catalog_does_not_know_shows_nothing()
    {
        var editor = EditorRig.Create().New(SampleScenario.Caretaker with { Notes = "Something a file author wrote." });

        Assert.False(editor.HasTemplateNotes);
        Assert.Equal(string.Empty, editor.TemplateNotes);
    }

    [Fact]
    public void Every_sentence_in_every_shipped_templates_note_has_catalog_wording()
    {
        var unknown = Savepoint.Core.Catalog.GameTemplates.All
            .Select(template => (template.Id, Left: TemplateNoteCatalog.UnknownPart(template.Definition.Notes)))
            .Where(entry => entry.Left.Length > 0)
            .Select(entry => $"{entry.Id}: {entry.Left}")
            .ToList();

        Assert.True(unknown.Count == 0, $"{unknown.Count} notes hold text with no catalog wording, such as: {string.Join(" | ", unknown.Take(3))}");
    }

    [Fact]
    public async Task Notes_from_a_definition_file_are_not_shown_as_template_notes()
    {
        var file = @"C:\Downloads\hollow-knight.json";
        var imported = new Savepoint.Core.Catalog.GameDefinition(Id: "from-file", Name: "Hollow Knight", Notes: "Saves are in Steam's userdata folder; add the saves folder by hand.");
        var rig = EditorRig.Create(new FakeBackend(ScenarioBuilder.Sample().WithImport(file, imported)));
        var editor = rig.New();
        rig.Dialogs.AnswerOpenFile(file);

        await editor.ImportCommand.ExecuteAsync(null);

        Assert.False(editor.HasTemplateNotes);
        Assert.Equal(string.Empty, editor.TemplateNotes);
    }

    [Fact]
    public async Task More_settings_opens_when_an_imported_file_sets_something_in_it_and_stays_closed_when_it_does_not()
    {
        var file = @"C:\Downloads\hollow-knight.json";
        var plain = new Savepoint.Core.Catalog.GameDefinition(Id: "plain", Name: "Plain Game", InstallIgnorePaths: Savepoint.Core.Catalog.RunTimeOutput.DefaultRules);
        var rig = EditorRig.Create(new FakeBackend(ScenarioBuilder.Sample().WithImport(file, plain)));
        var editor = rig.New();
        rig.Dialogs.AnswerOpenFile(file);
        await editor.ImportCommand.ExecuteAsync(null);
        Assert.False(editor.IsMoreSettingsOpen);

        var modded = plain with { IgnorePaths = ["**/*.tmp"] };
        var rig2 = EditorRig.Create(new FakeBackend(ScenarioBuilder.Sample().WithImport(file, modded)));
        var editor2 = rig2.New();
        rig2.Dialogs.AnswerOpenFile(file);
        await editor2.ImportCommand.ExecuteAsync(null);
        Assert.True(editor2.IsMoreSettingsOpen);
    }

    [Fact]
    public void More_settings_stays_closed_when_the_edited_game_only_has_the_default_install_rules()
    {
        var game = SampleScenario.Caretaker with { InstallIgnorePaths = Savepoint.Core.Catalog.RunTimeOutput.DefaultRules };
        var rig = EditorRig.Create(new FakeBackend(ScenarioBuilder.Empty().WithGame(game)));

        var editor = rig.Edit(game.Id);

        Assert.False(editor.IsMoreSettingsOpen);
    }

    [Fact]
    public async Task A_refused_Steam_app_id_opens_More_settings_again_on_Save_after_the_player_closed_it()
    {
        var rig = EditorRig.Create();
        var editor = rig.New();
        editor.Name = "Hollow Knight";
        editor.InstallPath = @"C:\Games\Hollow Knight";
        editor.ExePath = "hollow_knight.exe";
        editor.SteamAppIdText = "abc";
        Assert.True(editor.IsMoreSettingsOpen);
        editor.IsMoreSettingsOpen = false;

        var running = editor.SaveCommand.ExecuteAsync(null);
        while (!running.IsCompleted && rig.Dialogs.Shown.LastOrDefault() is Savepoint.App.Features.Editor.ConsentDialogViewModel { IsClosed: false } consent)
        {
            consent.ConfirmCommand.Execute(null);
        }

        await running;

        Assert.NotNull(editor.SteamAppIdError);
        Assert.True(editor.IsMoreSettingsOpen);
    }

    [Fact]
    public async Task An_open_cloud_answer_closes_when_the_store_says_Off()
    {
        var rig = EditorRig.Create();
        Games(rig).CloudSaves[SampleScenario.SkyrimId] = new CloudSaveInfo(CloudSaveState.Unknown, CloudStore.None);
        var editor = rig.Edit();
        await editor.LoadAsync();
        editor.CloudCheckDelay = (_, _) => Task.CompletedTask;
        editor.CloudSavesHelp.IsOpen = true;
        Games(rig).CloudSavesForForm = (_, _) => Task.FromResult(new CloudSaveInfo(CloudSaveState.Off, CloudStore.Steam));

        editor.InstallPath = @"D:\Games\Elsewhere";
        await EditorRig.Eventually(() => editor.HasCloudSavesUnknown ? null : editor);

        Assert.False(editor.HasCloudSavesUnknown);
        Assert.False(editor.HasCloudSavesNote);
        Assert.False(editor.CloudSavesHelp.IsOpen);
    }

    [Fact]
    public void A_new_game_without_notes_shows_no_template_line()
    {
        var rig = EditorRig.Create();

        Assert.False(rig.New().HasTemplateNotes);
        Assert.False(rig.New(SampleScenario.Caretaker).HasTemplateNotes);
    }

    [Fact]
    public void The_notes_a_player_keeps_on_an_existing_game_are_never_shown()
    {
        var game = SampleScenario.Caretaker with { Notes = "My own reminder." };
        var rig = EditorRig.Create(new FakeBackend(ScenarioBuilder.Empty().WithGame(game)));

        var editor = rig.Edit(game.Id);

        Assert.False(editor.HasTemplateNotes);
        Assert.Equal(string.Empty, editor.TemplateNotes);
    }

    [Theory]
    [InlineData("linux", true, 0)]
    [InlineData("linux", true, 1)]
    [InlineData("windows", false, 0)]
    [InlineData("windows", false, 1)]
    public void The_Linux_note_that_prefers_Through_Steam_and_the_Steam_warning_never_show_together(string os, bool inSteamLibrary, int choice)
    {
        var editor = EditorRig.Create(os: os).New();
        editor.InstallPath = inSteamLibrary ? "/home/p/.local/share/Steam/steamapps/common/Game" : @"C:\Games\Game";
        editor.LaunchChoice = choice;

        Assert.False(editor.ShowSteamStartNote && editor.IsSteamMethod);
    }
}
