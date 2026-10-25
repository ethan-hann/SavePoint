using Savepoint.App.Features.Common.Toasts;
using Savepoint.App.Features.Editor;
using Savepoint.App.Tests.Fakes;
using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;

namespace Savepoint.App.Tests.Features.Editor;

public class GameEditorTransferTests
{
    private const string _file = @"C:\Downloads\hollow-knight.json";

    private static readonly GameDefinition _imported = new(
        Id: "from-file-ab12",
        Name: "Hollow Knight",
        Install: new InstallLocation(@"C:\Games\Hollow Knight"),
        External: new ExternalLocation(@"{appData}\Hollow Knight"),
        Launch: new LaunchConfig(LaunchMethod.Exe, ExePath: "hollow_knight.exe", Args: ["-windowed"], ProcessNameForExitDetection: "hollow_knight.exe"),
        IgnorePaths: ["**/*.log"],
        ModPathHints: ["Mods"],
        Notes: "From the community.");

    private static EditorRig RigWithImport() =>
        EditorRig.Create(new FakeBackend(ScenarioBuilder.Sample().WithImport(_file, _imported)));

    private static ToastViewModel OnlyToast(EditorRig rig) => Assert.Single(rig.Toasts.Toasts);

    [Fact]
    public async Task Import_replaces_the_form_with_the_file_and_saves_nothing()
    {
        var rig = RigWithImport();
        var editor = rig.New();
        rig.Dialogs.AnswerOpenFile(_file);

        await editor.ImportCommand.ExecuteAsync(null);

        Assert.Equal("Hollow Knight", editor.Name);
        Assert.Equal(@"C:\Games\Hollow Knight", editor.InstallPath);
        Assert.Equal(@"{appData}\Hollow Knight", editor.SavesPath);
        Assert.Equal("hollow_knight.exe", editor.ExePath);
        Assert.Equal("hollow_knight.exe", editor.ProcessName);
        Assert.Equal(["-windowed"], editor.LaunchArgs.Values);
        Assert.Equal(["**/*.log"], editor.IgnoreRules.Values);
        Assert.Equal(["Mods"], editor.ModFolders.Values);
        Assert.Equal(_file, Assert.Single(rig.Calls("ImportDefinitionAsync")).Args[0]);
        Assert.Empty(rig.Calls("AddAsync"));
        Assert.Empty(rig.Shell.Calls);
    }

    [Fact]
    public async Task Importing_a_file_over_a_blank_form_takes_the_default_rules_note_away()
    {
        var rig = RigWithImport();
        var editor = rig.New();
        Assert.True(editor.HasDefaultRulesNote);
        rig.Dialogs.AnswerOpenFile(_file);

        await editor.ImportCommand.ExecuteAsync(null);

        Assert.False(editor.HasDefaultRulesNote);
        Assert.Equal(string.Empty, editor.DefaultRulesNote);
    }

    [Fact]
    public async Task Import_offers_definition_files_and_says_what_it_did()
    {
        var rig = RigWithImport();
        var editor = rig.New();
        rig.Dialogs.AnswerOpenFile(_file);

        await editor.ImportCommand.ExecuteAsync(null);

        var request = Assert.Single(rig.Dialogs.OpenFilePicks);
        Assert.Equal("Choose a game settings file", request.Title);
        Assert.Equal(["*.json"], Assert.Single(request.Filters!).Patterns);
        var toast = OnlyToast(rig);
        Assert.Equal(ToastKind.Success, toast.Message.Kind);
        Assert.Equal("Loaded Hollow Knight from the file. Nothing is saved until you choose Save changes.", toast.Text);
    }

    [Fact]
    public async Task Import_keeps_the_files_notes_and_gives_the_new_game_an_id_of_its_own()
    {
        var rig = RigWithImport();
        var editor = rig.New();
        rig.Dialogs.AnswerOpenFile(_file);
        await editor.ImportCommand.ExecuteAsync(null);

        var saving = editor.SaveCommand.ExecuteAsync(null);
        Assert.IsType<ConsentDialogViewModel>(Assert.Single(rig.Dialogs.Shown)).ConfirmCommand.Execute(null);
        await saving;

        var saved = rig.Saved("AddAsync");
        Assert.Equal("From the community.", saved.Notes);
        Assert.NotEqual(_imported.Id, saved.Id);
        Assert.StartsWith("hollow-knight-", saved.Id);
    }

    [Fact]
    public async Task Importing_after_a_failed_save_starts_a_new_id()
    {
        var rig = RigWithImport();
        var editor = rig.New();
        editor.Name = "Something else";
        editor.InstallPath = @"C:\Games\Else";
        editor.ExePath = "else.exe";
        rig.Backend.FailNext<IGamesApi>("AddAsync", new ErrorInfo("error.unknown", new Dictionary<string, object?>(), string.Empty));
        var first = editor.SaveCommand.ExecuteAsync(null);
        Assert.IsType<ConsentDialogViewModel>(rig.Dialogs.Shown[0]).ConfirmCommand.Execute(null);
        await first;
        rig.Dialogs.AnswerOpenFile(_file);

        await editor.ImportCommand.ExecuteAsync(null);
        var second = editor.SaveCommand.ExecuteAsync(null);
        Assert.IsType<ConsentDialogViewModel>(rig.Dialogs.Shown[^1]).ConfirmCommand.Execute(null);
        await second;

        var ids = rig.Calls("AddAsync").Select(call => ((GameDefinition)call.Args[0]!).Id).ToList();
        Assert.StartsWith("something-else-", ids[0]);
        Assert.StartsWith("hollow-knight-", ids[1]);
    }

    [Fact]
    public async Task Import_clears_the_errors_the_form_was_showing_and_looks_for_programs_in_the_new_folder()
    {
        var rig = RigWithImport();
        var editor = rig.New();
        editor.NameError = "Give the game a name.";
        editor.FormError = null;
        rig.Dialogs.AnswerOpenFile(_file);

        await editor.ImportCommand.ExecuteAsync(null);

        Assert.Null(editor.NameError);
        Assert.Equal(_imported.Install.Path, Assert.Single(rig.Backend.Calls.To<IGamesApi>("ListExecutablesAsync")).Args[0]);
    }

    [Fact]
    public async Task Cancelling_the_file_picker_changes_nothing_and_says_nothing()
    {
        var rig = RigWithImport();
        var editor = rig.New();

        await editor.ImportCommand.ExecuteAsync(null);

        Assert.Empty(rig.Calls("ImportDefinitionAsync"));
        Assert.Empty(rig.Toasts.Toasts);
        Assert.Equal(string.Empty, editor.Name);
    }

    [Fact]
    public async Task A_file_that_cannot_be_used_is_reported_with_the_reason_and_the_form_is_kept()
    {
        var rig = EditorRig.Create();
        var editor = rig.New();
        editor.Name = "Mine";
        rig.Dialogs.AnswerOpenFile(_file);
        rig.Backend.FailNext<IGamesApi>("ImportDefinitionAsync", ErrorInfo.From(new ValidationException([
            new ValidationIssue("name", ValidationMessageIds.GameNameRequired),
            new ValidationIssue("schemaVersion", ValidationMessageIds.GameSchemaVersionUnsupported),
        ])));

        await editor.ImportCommand.ExecuteAsync(null);

        var toast = OnlyToast(rig);
        Assert.Equal(ToastKind.Error, toast.Message.Kind);
        Assert.Equal("SavePoint couldn't use that file.", toast.Text);
        Assert.Equal("Give the game a name. This game's file is from a different version of SavePoint.", toast.Details);
        Assert.Equal("Mine", editor.Name);
    }

    [Fact]
    public async Task A_file_that_cannot_be_read_at_all_says_so_with_the_general_message()
    {
        var rig = EditorRig.Create();
        var editor = rig.New();
        rig.Dialogs.AnswerOpenFile(_file);

        await editor.ImportCommand.ExecuteAsync(null);

        var toast = OnlyToast(rig);
        Assert.Equal("SavePoint couldn't use that file.", toast.Text);
        Assert.Equal("Something went wrong. Try again.", toast.Details);
    }

    [Fact]
    public async Task Import_is_offered_only_when_adding_and_Export_only_when_editing()
    {
        var rig = EditorRig.Create();
        var adding = rig.New();
        var editing = rig.Edit();

        Assert.True(adding.CanImport);
        Assert.False(adding.CanExport);
        Assert.True(adding.ImportCommand.CanExecute(null));
        Assert.False(adding.ExportCommand.CanExecute(null));
        Assert.False(editing.CanImport);
        Assert.True(editing.CanExport);
        Assert.False(editing.ImportCommand.CanExecute(null));
        Assert.True(editing.ExportCommand.CanExecute(null));
        await editing.ExportCommand.ExecuteAsync(null);
        Assert.Empty(rig.Calls("ImportDefinitionAsync"));
    }

    [Fact]
    public async Task Export_writes_the_game_to_the_chosen_file_and_says_where()
    {
        var rig = EditorRig.Create();
        var editor = rig.Edit();
        rig.Dialogs.AnswerSaveFile(@"C:\Exports\skyrim.json");

        await editor.ExportCommand.ExecuteAsync(null);

        var request = Assert.Single(rig.Dialogs.SaveFilePicks);
        Assert.Equal("Export Skyrim Special Edition", request.Title);
        Assert.Equal($"{SampleScenario.SkyrimId}.json", request.SuggestedName);
        Assert.Equal(["*.json"], Assert.Single(request.Filters!).Patterns);
        var call = Assert.Single(rig.Calls("ExportDefinitionAsync"));
        Assert.Equal([SampleScenario.SkyrimId, @"C:\Exports\skyrim.json"], call.Args);
        var toast = OnlyToast(rig);
        Assert.Equal(ToastKind.Success, toast.Message.Kind);
        Assert.Equal("Exported Skyrim Special Edition.", toast.Text);
        Assert.Equal(@"C:\Exports\skyrim.json", toast.Details);
    }

    [Fact]
    public async Task Exporting_with_unsaved_changes_says_they_are_not_in_the_file()
    {
        var rig = EditorRig.Create();
        var editor = rig.Edit();
        editor.Name = "Skyrim SE";
        rig.Dialogs.AnswerSaveFile(@"C:\Exports\skyrim.json");

        await editor.ExportCommand.ExecuteAsync(null);

        var toast = OnlyToast(rig);
        Assert.Equal("Exported the saved version of Skyrim Special Edition. Changes you haven't saved yet aren't in the file.", toast.Text);
        Assert.Equal(@"C:\Exports\skyrim.json", toast.Details);
    }

    [Fact]
    public async Task Cancelling_the_save_picker_exports_nothing_and_says_nothing()
    {
        var rig = EditorRig.Create();
        var editor = rig.Edit();

        await editor.ExportCommand.ExecuteAsync(null);

        Assert.Empty(rig.Calls("ExportDefinitionAsync"));
        Assert.Empty(rig.Toasts.Toasts);
    }

    [Fact]
    public async Task A_failed_export_is_reported_with_the_reason()
    {
        var rig = EditorRig.Create();
        var editor = rig.Edit();
        rig.Dialogs.AnswerSaveFile(@"C:\Exports\skyrim.json");
        rig.Backend.FailNext<IGamesApi>("ExportDefinitionAsync", new ErrorInfo("error.unknown", new Dictionary<string, object?>(), "access denied"));

        await editor.ExportCommand.ExecuteAsync(null);

        var toast = OnlyToast(rig);
        Assert.Equal(ToastKind.Error, toast.Message.Kind);
        Assert.Equal("SavePoint couldn't export that game.", toast.Text);
        Assert.Equal("Something went wrong. Try again.", toast.Details);
    }
}
