using CommunityToolkit.Mvvm.Input;
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
}
