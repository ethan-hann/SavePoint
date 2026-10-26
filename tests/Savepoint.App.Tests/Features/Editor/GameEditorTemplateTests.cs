using CommunityToolkit.Mvvm.Input;
using Savepoint.App.Features.Common.Toasts;
using Savepoint.App.Features.Editor;
using Savepoint.App.Features.Shell;
using Savepoint.App.Tests.Fakes;
using Savepoint.Core.Catalog;

namespace Savepoint.App.Tests.Features.Editor;

// Built-in templates from inside the editor: the Name field's search and the header's "Pick from the game list".
public class GameEditorTemplateTests
{
    private static readonly GameTemplate _skyrim = GameTemplates.All.Single(template => template.Id == "skyrim-se");

    private static readonly GameDefinition _skyrimDraft = _skyrim.ToDraft();

    private static readonly GameDefinition _fromFile = new(
        Id: "from-file-ab12",
        Name: "Hollow Knight",
        Install: new InstallLocation(@"C:\Games\Hollow Knight"),
        Notes: "From the community.");

    private const string _file = @"C:\Downloads\hollow-knight.json";

    // Types into the Name field and takes the Skyrim entry from the list.
    private static async Task PickSkyrimFromNameSearchAsync(GameEditorViewModel editor, string typed = "skyr")
    {
        editor.Name = typed;
        var match = Assert.Single(editor.NameMatches, item => item.Name == _skyrim.DisplayName);
        await ((IAsyncRelayCommand)match.Choose).ExecuteAsync(null);
    }

    // Opens the header's picker and answers it with the Skyrim entry.
    private static async Task PickSkyrimFromHeaderAsync(EditorRig rig, GameEditorViewModel editor)
    {
        var pending = editor.PickTemplateCommand.ExecuteAsync(null);
        var picker = Assert.IsType<TemplatePickerViewModel>(Assert.Single(rig.Dialogs.Shown));

        // The picker opens on the name typed so far; the player clears it to see the whole list.
        picker.SearchText = string.Empty;
        picker.Templates.Single(item => item.Name == _skyrim.DisplayName).Choose.Execute(null);
        await pending;
    }

    private static async Task SaveAsync(EditorRig rig, GameEditorViewModel editor)
    {
        var running = editor.SaveCommand.ExecuteAsync(null);
        while (!running.IsCompleted && rig.Dialogs.Shown.LastOrDefault() is ConsentDialogViewModel { IsClosed: false } consent)
        {
            consent.ConfirmCommand.Execute(null);
        }

        await running;
    }

    [Fact]
    public void Typing_two_letters_of_a_name_lists_the_templates_that_match()
    {
        var editor = EditorRig.Create().New();

        editor.Name = "skyr";

        Assert.Contains(_skyrim.DisplayName, editor.NameMatches.Select(item => item.Name));
        Assert.True(editor.HasNameMatches);
    }

    [Fact]
    public void The_list_is_short_however_many_templates_match()
    {
        var editor = EditorRig.Create().New();

        editor.Name = "the";

        Assert.InRange(editor.NameMatches.Count, 1, 8);
    }

    [Fact]
    public void One_letter_lists_nothing()
    {
        var editor = EditorRig.Create().New();

        editor.Name = "s";

        Assert.Empty(editor.NameMatches);
        Assert.False(editor.HasNameMatches);
    }

    [Fact]
    public void Clearing_the_name_closes_the_list()
    {
        var editor = EditorRig.Create().New();
        editor.Name = "skyr";

        editor.Name = "s";

        Assert.False(editor.HasNameMatches);
    }

    [Fact]
    public void Editing_a_game_never_offers_templates()
    {
        var editor = EditorRig.Create().Edit();

        editor.Name = "skyr";

        Assert.Empty(editor.NameMatches);
        Assert.False(editor.HasNameMatches);
    }

    [Fact]
    public void A_form_opened_on_a_template_does_not_list_matches_for_its_own_name()
    {
        var editor = EditorRig.Create().New(_skyrimDraft);

        Assert.Empty(editor.NameMatches);
    }

    [Fact]
    public void Escape_closes_the_list_until_the_name_changes_again()
    {
        var editor = EditorRig.Create().New();
        editor.Name = "skyr";

        editor.DismissNameMatchesCommand.Execute(null);
        Assert.False(editor.HasNameMatches);

        editor.Name = "skyri";
        Assert.True(editor.HasNameMatches);
    }

    [Fact]
    public async Task Picking_a_match_names_the_game_and_fills_the_details_still_empty()
    {
        var editor = EditorRig.Create().New();

        await PickSkyrimFromNameSearchAsync(editor);

        Assert.Equal(_skyrim.DisplayName, editor.Name);
        Assert.Equal(_skyrimDraft.External?.Path ?? string.Empty, editor.SavesPath);
        Assert.Equal("SkyrimSELauncher.exe", editor.ExePath);
        Assert.Equal("SkyrimSE.exe", editor.ProcessName);
        Assert.Equal("489830", editor.SteamAppIdText);
        Assert.Equal(_skyrim.Definition.IgnorePaths, editor.IgnoreRules.Values);
        Assert.Equal(_skyrim.Definition.ModPathHints, editor.ModFolders.Values);
        Assert.False(editor.HasNameMatches);
        Assert.False(editor.HasDefaultRulesNote);
    }

    [Fact]
    public async Task Picking_a_match_keeps_what_the_player_already_typed()
    {
        var editor = EditorRig.Create().New();
        editor.InstallPath = @"D:\Games\Skyrim";
        editor.SavesPath = @"D:\Saves";
        editor.ExePath = "mine.exe";
        editor.ProcessName = "mine";
        editor.SteamAppIdText = "1";
        editor.IgnoreRules.SetValues(["*.tmp"]);
        editor.ModFolders.SetValues(["Mine"]);

        await PickSkyrimFromNameSearchAsync(editor);

        Assert.Equal(@"D:\Games\Skyrim", editor.InstallPath);
        Assert.Equal(@"D:\Saves", editor.SavesPath);
        Assert.Equal("mine.exe", editor.ExePath);
        Assert.Equal("mine", editor.ProcessName);
        Assert.Equal("1", editor.SteamAppIdText);
        Assert.Equal(["*.tmp"], editor.IgnoreRules.Values);
        Assert.Equal(["Mine"], editor.ModFolders.Values);
    }

    [Fact]
    public async Task A_game_started_this_way_saves_with_the_templates_notes_and_rules()
    {
        var rig = EditorRig.Create();
        var editor = rig.New();
        await PickSkyrimFromNameSearchAsync(editor);
        editor.InstallPath = @"D:\Games\Skyrim";

        await SaveAsync(rig, editor);

        var saved = rig.Saved("AddAsync");
        Assert.Equal(_skyrim.Definition.Notes, saved.Notes);
        Assert.Equal(489830, saved.SteamAppId);
        Assert.Matches("^[a-z0-9-]+-[a-z0-9]{4}$", saved.Id);
        Assert.Equal(_skyrim.Definition.IgnorePaths, rig.Backend.Games.LastSuppliedRules);
    }

    [Fact]
    public async Task Picking_a_match_keeps_notes_the_form_already_carries()
    {
        var rig = EditorRig.Create(new FakeBackend(ScenarioBuilder.Sample().WithImport(_file, _fromFile)));
        var editor = rig.New();
        rig.Dialogs.AnswerOpenFile(_file);
        await editor.ImportCommand.ExecuteAsync(null);

        await PickSkyrimFromNameSearchAsync(editor, "skyr");
        await SaveAsync(rig, editor);

        Assert.Equal("From the community.", rig.Saved("AddAsync").Notes);
    }

    [Fact]
    public async Task Picking_a_match_marks_the_form_as_changed()
    {
        var editor = EditorRig.Create().New();
        Assert.False(editor.IsDirty);

        await PickSkyrimFromNameSearchAsync(editor);

        Assert.True(editor.IsDirty);
    }

    [Fact]
    public void The_header_offers_the_game_list_only_while_adding()
    {
        var rig = EditorRig.Create();

        Assert.True(rig.New().CanPickTemplate);
        Assert.False(rig.Edit().CanPickTemplate);
    }

    [Fact]
    public async Task The_header_button_opens_the_template_picker_and_fills_an_empty_form()
    {
        var rig = EditorRig.Create();
        var editor = rig.New();

        await PickSkyrimFromHeaderAsync(rig, editor);

        Assert.Equal(_skyrim.DisplayName, editor.Name);
        Assert.Equal("SkyrimSELauncher.exe", editor.ExePath);
        Assert.Equal("489830", editor.SteamAppIdText);
        Assert.Empty(rig.Calls("AddAsync"));
    }

    [Fact]
    public async Task The_header_button_keeps_a_name_and_other_details_the_player_typed()
    {
        var rig = EditorRig.Create();
        var editor = rig.New();
        editor.Name = "My modded Skyrim";
        editor.ExePath = "mine.exe";

        await PickSkyrimFromHeaderAsync(rig, editor);

        Assert.Equal("My modded Skyrim", editor.Name);
        Assert.Equal("mine.exe", editor.ExePath);
        Assert.Equal("SkyrimSE.exe", editor.ProcessName);
    }

    [Fact]
    public async Task Backing_out_of_the_header_picker_changes_nothing()
    {
        var rig = EditorRig.Create();
        var editor = rig.New();

        var pending = editor.PickTemplateCommand.ExecuteAsync(null);
        Assert.IsType<TemplatePickerViewModel>(Assert.Single(rig.Dialogs.Shown)).RequestDismiss();
        await pending;

        Assert.False(editor.IsDirty);
    }

    [Fact]
    public async Task Choosing_a_file_in_the_header_picker_loads_it_like_Import()
    {
        var rig = EditorRig.Create(new FakeBackend(ScenarioBuilder.Sample().WithImport(_file, _fromFile)));
        var editor = rig.New();
        rig.Dialogs.AnswerOpenFile(_file);

        var pending = editor.PickTemplateCommand.ExecuteAsync(null);
        Assert.IsType<TemplatePickerViewModel>(Assert.Single(rig.Dialogs.Shown)).OpenFileCommand.Execute(null);
        await pending;

        Assert.Equal("Hollow Knight", editor.Name);
        Assert.Equal(_file, Assert.Single(rig.Calls("ImportDefinitionAsync")).Args[0]);
    }

    // ---- The install-only rules follow the game-wide rules

    [Fact]
    public async Task On_a_blank_form_the_install_only_rules_are_the_templates_own_draft()
    {
        var editor = EditorRig.Create().New();

        await PickSkyrimFromNameSearchAsync(editor);

        Assert.Equal(_skyrimDraft.InstallIgnorePaths, editor.InstallIgnoreRules.Values);
    }

    [Fact]
    public async Task A_default_rule_the_templates_game_wide_list_would_cover_stays_in_the_install_list_when_the_player_kept_their_own_game_wide_list()
    {
        var editor = EditorRig.Create().New();
        editor.IgnoreRules.SetValues(["*.tmp"]);

        await PickSkyrimFromNameSearchAsync(editor);

        var expected = RunTimeOutput.Merge(_skyrim.Definition.InstallIgnorePaths, RunTimeOutput.DefaultRules.Where(rule => rule != "*.tmp"));
        Assert.Equal(expected, editor.InstallIgnoreRules.Values);
        Assert.Contains("**/*.log", editor.InstallIgnoreRules.Values);
    }

    [Fact]
    public async Task A_default_rule_the_player_removed_does_not_come_back_but_the_templates_own_install_rules_are_added()
    {
        var zed = Custom("Zed", "zed.exe", "zed", 7, _userdataNote);
        var rig = RigWith(zed with { Definition = zed.Definition with { InstallIgnorePaths = ["Cache/**"] } });
        var editor = rig.New();
        editor.InstallIgnoreRules.SetValues([.. RunTimeOutput.DefaultRules.Where(rule => rule != "**/*.mdmp")]);

        editor.Name = "zed";
        await ((IAsyncRelayCommand)editor.NameMatches[0].Choose).ExecuteAsync(null);

        Assert.DoesNotContain("**/*.mdmp", editor.InstallIgnoreRules.Values);
        Assert.Contains("Cache/**", editor.InstallIgnoreRules.Values);
        Assert.Contains("**/*.log", editor.InstallIgnoreRules.Values);
    }

    // ---- Picking after picking

    private const string _userdataNote = "Saves are in Steam's userdata folder; add the saves folder by hand.";
    private const string _userdataText = "This game's saves are in Steam's userdata folder. Choose the saves folder yourself.";
    private const string _unconfirmedNote = "We couldn't confirm where this game keeps its saves; add the saves folder by hand.";
    private const string _unconfirmedText = "SavePoint couldn't confirm where this game keeps its saves. Choose the saves folder yourself.";

    private static FakeGamesApi Games(EditorRig rig) => Assert.IsType<FakeGamesApi>(rig.Backend.Games);

    private static EditorRig RigWith(params GameTemplate[] templates)
    {
        var rig = EditorRig.Create();
        Games(rig).TemplatesOverride = templates;
        return rig;
    }

    private static GameTemplate Custom(string name, string exe, string process, int appId, string notes, string[]? ignore = null, string[]? mods = null, string[]? args = null, string? uri = null)
    {
        var id = name.ToLowerInvariant().Replace(' ', '-');
        var launch = uri is null
            ? new LaunchConfig(LaunchMethod.Exe, ExePath: exe, Args: args ?? [], ProcessNameForExitDetection: process)
            : new LaunchConfig(LaunchMethod.Uri, Uri: uri, Args: args ?? [], ProcessNameForExitDetection: process);
        return new GameTemplate(id, name, new GameDefinition(Id: id, Name: name, Launch: launch, IgnorePaths: ignore ?? [], ModPathHints: mods ?? [], Notes: notes, SteamAppId: appId));
    }

    private static async Task PickAsync(GameEditorViewModel editor, string typed, string name)
    {
        editor.Name = typed;
        var match = Assert.Single(editor.NameMatches, item => item.Name == name);
        await ((IAsyncRelayCommand)match.Choose).ExecuteAsync(null);
    }

    [Fact]
    public async Task Picking_another_template_replaces_what_the_first_one_filled()
    {
        var alpha = Custom("Alpha Game", "a.exe", "a", 1, _userdataNote, ["a.tmp"], ["ModA"], ["-a"]);
        var beta = Custom("Beta Game", "b.exe", "b", 2, _unconfirmedNote, ["b.tmp"], ["ModB"], ["-b"]);
        var editor = RigWith(alpha, beta).New();
        await PickAsync(editor, "alp", "Alpha Game");
        Assert.Equal(_userdataText, editor.TemplateNotes);

        await PickAsync(editor, "bet", "Beta Game");

        Assert.Equal("Beta Game", editor.Name);
        Assert.Equal("b.exe", editor.ExePath);
        Assert.Equal("b", editor.ProcessName);
        Assert.Equal(["-b"], editor.LaunchArgs.Values);
        Assert.Equal(["b.tmp"], editor.IgnoreRules.Values);
        Assert.Equal(["ModB"], editor.ModFolders.Values);
        Assert.Equal("2", editor.SteamAppIdText);
        Assert.Equal(_unconfirmedText, editor.TemplateNotes);
    }

    [Fact]
    public async Task Picking_another_template_keeps_what_the_player_changed_after_the_first()
    {
        var alpha = Custom("Alpha Game", "a.exe", "a", 1, _userdataNote, ["a.tmp"], ["ModA"]);
        var beta = Custom("Beta Game", "b.exe", "b", 2, _unconfirmedNote, ["b.tmp"], ["ModB"]);
        var editor = RigWith(alpha, beta).New();
        await PickAsync(editor, "alp", "Alpha Game");
        editor.ExePath = "mine.exe";
        editor.IgnoreRules.SetValues(["a.tmp", "mine.tmp"]);

        await PickAsync(editor, "bet", "Beta Game");

        Assert.Equal("mine.exe", editor.ExePath);
        Assert.Equal(["a.tmp", "mine.tmp"], editor.IgnoreRules.Values);
        Assert.Equal("b", editor.ProcessName);
    }

    [Fact]
    public async Task Picking_another_template_from_the_header_replaces_the_name_the_first_one_gave()
    {
        var alpha = Custom("Alpha Game", "a.exe", "a", 1, _userdataNote);
        var beta = Custom("Beta Game", "b.exe", "b", 2, _unconfirmedNote);
        var rig = RigWith(alpha, beta);
        var editor = rig.New();
        await PickAsync(editor, "alp", "Alpha Game");

        var pending = editor.PickTemplateCommand.ExecuteAsync(null);
        var picker = Assert.IsType<TemplatePickerViewModel>(rig.Dialogs.Shown[0]);
        picker.SearchText = string.Empty;
        picker.Templates.Single(item => item.Name == "Beta Game").Choose.Execute(null);
        await pending;

        Assert.Equal("Beta Game", editor.Name);
    }

    // ---- How the game starts

    [Fact]
    public async Task A_template_that_starts_through_a_link_chooses_Through_Steam_and_fills_the_link()
    {
        var link = Custom("Link Game", "", "link", 42, _userdataNote, uri: "steam://rungameid/42");
        var editor = RigWith(link).New();

        await PickAsync(editor, "link", "Link Game");

        Assert.Equal(1, editor.LaunchChoice);
        Assert.Equal("steam://rungameid/42", editor.Uri);
        Assert.Equal(string.Empty, editor.ExePath);
    }

    [Fact]
    public async Task A_link_template_picked_after_the_player_typed_a_start_file_keeps_the_start_file_and_its_choice()
    {
        var link = Custom("Link Game", "", "link", 42, _userdataNote, uri: "steam://rungameid/42");
        var editor = RigWith(link).New();
        editor.ExePath = "mine.exe";

        await PickAsync(editor, "link", "Link Game");

        Assert.Equal(0, editor.LaunchChoice);
        Assert.Equal("mine.exe", editor.ExePath);
        Assert.Equal("steam://rungameid/42", editor.Uri);
    }

    [Fact]
    public async Task A_player_who_chose_Through_Steam_but_typed_nothing_keeps_that_choice()
    {
        var exe = Custom("Exe Game", "e.exe", "e", 5, _userdataNote);
        var editor = RigWith(exe).New();
        editor.LaunchChoice = 1;

        await PickAsync(editor, "exe", "Exe Game");

        Assert.Equal(1, editor.LaunchChoice);
    }

    [Fact]
    public async Task Launch_options_fill_only_while_the_list_is_empty()
    {
        var args = Custom("Arg Game", "g.exe", "g", 6, _userdataNote, args: ["-windowed", "-skipintro"]);
        var rig = RigWith(args);
        var blank = rig.New();
        await PickAsync(blank, "arg", "Arg Game");
        Assert.Equal(["-windowed", "-skipintro"], blank.LaunchArgs.Values);

        var typed = rig.New();
        typed.LaunchArgs.SetValues(["-mine"]);
        await PickAsync(typed, "arg", "Arg Game");
        Assert.Equal(["-mine"], typed.LaunchArgs.Values);
    }

    [Fact]
    public async Task The_Steam_app_id_is_not_filled_when_the_player_typed_a_link_for_another_id()
    {
        var editor = EditorRig.Create().New();
        editor.LaunchChoice = 1;
        editor.Uri = "steam://run/999";

        await PickSkyrimFromNameSearchAsync(editor);

        Assert.Equal(string.Empty, editor.SteamAppIdText);
    }

    [Fact]
    public async Task The_Steam_app_id_is_filled_when_the_typed_link_names_the_same_id()
    {
        var editor = EditorRig.Create().New();
        editor.LaunchChoice = 1;
        editor.Uri = "steam://run/489830";

        await PickSkyrimFromNameSearchAsync(editor);

        Assert.Equal("489830", editor.SteamAppIdText);
    }

    // ---- The toast, More settings and the header picker's search

    [Fact]
    public async Task A_pick_says_what_it_did_and_that_nothing_is_saved()
    {
        var rig = EditorRig.Create();
        var editor = rig.New();

        await PickSkyrimFromNameSearchAsync(editor);

        var toast = Assert.Single(rig.Toasts.Toasts);
        Assert.Equal(ToastKind.Success, toast.Message.Kind);
        Assert.Equal($"Filled in the empty details from {_skyrim.DisplayName}. Nothing is saved until you choose Save changes.", toast.Text);
    }

    [Fact]
    public async Task A_pick_leaves_More_settings_closed()
    {
        var editor = EditorRig.Create().New();

        await PickSkyrimFromNameSearchAsync(editor);

        Assert.False(editor.IsMoreSettingsOpen);
    }

    [Fact]
    public async Task The_header_picker_opens_with_the_name_typed_so_far_as_its_search()
    {
        var rig = EditorRig.Create();
        var editor = rig.New();
        editor.Name = "skyrim spec";
        editor.DismissNameMatchesCommand.Execute(null);

        var pending = editor.PickTemplateCommand.ExecuteAsync(null);
        var picker = Assert.IsType<TemplatePickerViewModel>(Assert.Single(rig.Dialogs.Shown));

        Assert.Equal("skyrim spec", picker.SearchText);
        Assert.Equal([_skyrim.DisplayName], picker.Templates.Select(item => item.Name));
        picker.RequestDismiss();
        await pending;
    }

    [Fact]
    public async Task The_header_picker_lists_everything_when_no_name_is_typed()
    {
        var rig = EditorRig.Create();
        var editor = rig.New();

        var pending = editor.PickTemplateCommand.ExecuteAsync(null);
        var picker = Assert.IsType<TemplatePickerViewModel>(Assert.Single(rig.Dialogs.Shown));

        Assert.Equal(string.Empty, picker.SearchText);
        Assert.Equal(rig.Backend.Games.Templates.Count, picker.Templates.Count);
        picker.RequestDismiss();
        await pending;
    }

    // ---- The match list for the keyboard and screen readers

    [Fact]
    public void The_list_says_how_many_games_it_found()
    {
        var rig = RigWith(Custom("Alpha One", "a", "a", 1, _userdataNote), Custom("Alpha Two", "b", "b", 2, _userdataNote), Custom("Beta", "c", "c", 3, _userdataNote));
        var editor = rig.New();

        editor.Name = "alp";
        Assert.Equal("2 games found. Choose one to fill in the details.", editor.NameMatchesAnnouncement);

        editor.Name = "alpha o";
        Assert.Equal("1 game found. Choose one to fill in the details.", editor.NameMatchesAnnouncement);

        editor.Name = "zzz";
        Assert.Equal(string.Empty, editor.NameMatchesAnnouncement);
    }

    [Fact]
    public void Each_match_says_in_its_accessible_name_that_it_fills_the_form()
    {
        var editor = EditorRig.Create().New();

        editor.Name = "skyrim special";

        Assert.Equal($"Fill in from {_skyrim.DisplayName}", Assert.Single(editor.NameMatches).AutomationName);
    }

    [Fact]
    public async Task Choosing_the_top_match_takes_the_first_entry_of_the_list()
    {
        var editor = EditorRig.Create().New();
        editor.Name = "skyrim special";
        Assert.True(editor.ChooseTopNameMatchCommand.CanExecute(null));

        await editor.ChooseTopNameMatchCommand.ExecuteAsync(null);

        Assert.Equal(_skyrim.DisplayName, editor.Name);
        Assert.Equal("489830", editor.SteamAppIdText);
    }

    [Fact]
    public void Enter_and_Escape_do_nothing_while_no_list_shows()
    {
        var editor = EditorRig.Create().New();
        editor.Name = "s";

        Assert.False(editor.ChooseTopNameMatchCommand.CanExecute(null));
        Assert.False(editor.DismissNameMatchesCommand.CanExecute(null));

        editor.Name = "skyr";
        Assert.True(editor.DismissNameMatchesCommand.CanExecute(null));
    }

    [Fact]
    public async Task A_pick_asks_for_the_focus_to_go_back_to_the_name_field()
    {
        var editor = EditorRig.Create().New();
        var asked = 0;
        editor.FocusNameRequested += (_, _) => asked++;

        await PickSkyrimFromNameSearchAsync(editor);

        Assert.Equal(1, asked);
    }

    [Fact]
    public void The_matches_are_ranked_by_how_much_of_the_name_was_typed()
    {
        var rig = RigWith(Custom("Old Sid Civ", "a", "a", 1, _userdataNote), Custom("Sid Meier's Civilization", "b", "b", 2, _userdataNote), Custom("Sid Civilization Rebirth", "c", "c", 3, _userdataNote));
        var editor = rig.New();

        editor.Name = "sid civ";

        Assert.Equal(["Sid Civilization Rebirth", "Sid Meier's Civilization", "Old Sid Civ"], editor.NameMatches.Select(item => item.Name));
    }

    [Fact]
    public void A_word_typed_without_punctuation_finds_a_game_whose_name_has_it()
    {
        var editor = EditorRig.Create().New();

        editor.Name = "halflife";

        Assert.Contains(editor.NameMatches, item => item.Name.StartsWith("Half-Life", StringComparison.Ordinal));
    }
}
