using Savepoint.App.Features.Editor;
using Savepoint.App.Tests.Fakes;
using Savepoint.Core.Api;
using Savepoint.Core.Catalog;

namespace Savepoint.App.Tests.Features.Editor;

public class GameEditorSaveTests
{
    // Every folder a new game names is confirmed in its own dialog, which these tests answer.
    private static async Task Save(EditorRig rig, GameEditorViewModel editor)
    {
        var running = editor.SaveCommand.ExecuteAsync(null);
        while (!running.IsCompleted && rig.Dialogs.Shown.LastOrDefault() is ConsentDialogViewModel { IsClosed: false } consent)
        {
            consent.ConfirmCommand.Execute(null);
        }

        await running;
    }

    private static GameEditorViewModel Filled(EditorRig rig)
    {
        var editor = rig.New();
        editor.Name = "  Hollow Knight  ";
        editor.InstallPath = @"  C:\Games\Hollow Knight  ";
        editor.SavesPath = @"{appData}\Hollow Knight";
        editor.ExePath = "hollow_knight.exe";
        editor.ProcessName = "hollow_knight.exe";
        editor.IgnoreRules.SetValues(["**/*.log"]);
        editor.ModFolders.SetValues(["Mods"]);
        return editor;
    }

    [Fact]
    public async Task Saving_a_new_game_adds_the_trimmed_definition_with_a_generated_id()
    {
        var rig = EditorRig.Create();
        var editor = Filled(rig);

        await Save(rig, editor);

        var saved = rig.Saved("AddAsync");
        Assert.Matches("^hollow-knight-[a-z0-9]{4}$", saved.Id);
        Assert.Equal("Hollow Knight", saved.Name);
        Assert.Equal(@"C:\Games\Hollow Knight", saved.Install.Path);
        Assert.Equal(new ExternalLocation(@"{appData}\Hollow Knight", ExternalRole.Saves), saved.External);
        Assert.Equal(new LaunchConfig(LaunchMethod.Exe, ExePath: "hollow_knight.exe", ProcessNameForExitDetection: "hollow_knight.exe"), saved.Launch);
        Assert.Equal(["**/*.log"], saved.IgnorePaths);
        Assert.Equal(["Mods"], saved.ModPathHints);
        Assert.Equal(GameDefinition.CurrentSchemaVersion, saved.SchemaVersion);
    }

    [Fact]
    public async Task A_saved_new_game_refreshes_the_library_selects_the_game_and_closes_the_pane_in_that_order()
    {
        var rig = EditorRig.Create();
        var editor = Filled(rig);

        await Save(rig, editor);

        var saved = rig.Saved("AddAsync");
        Assert.Equal(["refresh", $"select:{saved.Id}", "close"], rig.Shell.Calls);
    }

    [Fact]
    public async Task Blank_optional_fields_are_left_out_of_the_definition()
    {
        var rig = EditorRig.Create();
        var editor = Filled(rig);
        editor.SavesPath = "   ";
        editor.ProcessName = "";

        await Save(rig, editor);

        var saved = rig.Saved("AddAsync");
        Assert.Null(saved.External);
        Assert.Null(saved.Launch.ProcessNameForExitDetection);
    }

    [Fact]
    public async Task The_Steam_choice_saves_the_link_and_drops_the_executable()
    {
        var rig = EditorRig.Create();
        var editor = Filled(rig);
        editor.LaunchChoice = 1;
        editor.Uri = "  steam://run/367520  ";
        editor.LaunchArgs.SetValues(["-windowed"]);

        await Save(rig, editor);

        var launch = rig.Saved("AddAsync").Launch;
        Assert.Equal(LaunchMethod.Uri, launch.Method);
        Assert.Equal("steam://run/367520", launch.Uri);
        Assert.Null(launch.ExePath);
        Assert.Equal(["-windowed"], launch.Args);
    }

    [Fact]
    public async Task The_executable_choice_saves_the_path_and_drops_the_link()
    {
        var rig = EditorRig.Create();
        var editor = Filled(rig);
        editor.Uri = "steam://run/367520";

        await Save(rig, editor);

        var launch = rig.Saved("AddAsync").Launch;
        Assert.Equal(LaunchMethod.Exe, launch.Method);
        Assert.Null(launch.Uri);
    }

    [Fact]
    public async Task Rules_added_with_the_chips_are_saved()
    {
        var rig = EditorRig.Create();
        var editor = Filled(rig);
        editor.IgnoreRules.BeginAddCommand.Execute(null);
        editor.IgnoreRules.NewText = "**/Crash Logs/**";
        editor.IgnoreRules.CommitAddCommand.Execute(null);

        await Save(rig, editor);

        Assert.Equal(["**/*.log", "**/Crash Logs/**"], rig.Saved("AddAsync").IgnorePaths);
    }

    [Fact]
    public async Task Saving_an_edited_game_updates_it_under_its_own_id_and_never_adds()
    {
        var rig = EditorRig.Create();
        var editor = rig.Edit();
        editor.Name = "Skyrim SE";

        await Save(rig, editor);

        var saved = rig.Saved("UpdateAsync");
        Assert.Equal(SampleScenario.SkyrimId, saved.Id);
        Assert.Equal("Skyrim SE", saved.Name);
        Assert.Empty(rig.Calls("AddAsync"));
        Assert.Equal(["refresh", $"select:{SampleScenario.SkyrimId}", "close"], rig.Shell.Calls);
    }

    [Fact]
    public async Task Game_notes_survive_an_edit_untouched()
    {
        var withNotes = SampleScenario.Skyrim with { Notes = "Direct-exe launch preferred." };
        var rig = EditorRig.Create(new FakeBackend(ScenarioBuilder.Empty().WithGame(withNotes)));
        var editor = rig.Edit(withNotes.Id);
        editor.Name = "Skyrim SE";

        await Save(rig, editor);

        Assert.Equal("Direct-exe launch preferred.", rig.Saved("UpdateAsync").Notes);
    }

    [Fact]
    public async Task Notes_of_a_template_travel_with_the_new_game()
    {
        var rig = EditorRig.Create();
        var template = rig.Backend.Games.Templates.First(t => t.Definition.Notes is not null).ToDraft();
        var editor = rig.New(template);
        // Templates leave the install folder for the player to pick.
        editor.InstallPath = @"C:\Games\SkyrimSE";

        await Save(rig, editor);

        Assert.Equal(template.Notes, rig.Saved("AddAsync").Notes);
    }

    [Fact]
    public async Task A_new_game_gets_a_fresh_id_even_when_the_prefill_has_one()
    {
        var rig = EditorRig.Create();
        var template = rig.Backend.Games.Templates[0].ToDraft();
        var editor = rig.New(template);
        editor.InstallPath = @"C:\Games\SkyrimSE";

        await Save(rig, editor);

        Assert.NotEqual(template.Id, rig.Saved("AddAsync").Id);
    }

    [Fact]
    public async Task One_new_game_keeps_the_same_id_across_retries()
    {
        var rig = EditorRig.Create();
        var editor = Filled(rig);
        rig.Backend.FailNext<IGamesApi>("AddAsync", new Savepoint.Core.Errors.ErrorInfo("error.unknown", new Dictionary<string, object?>(), string.Empty));

        await Save(rig, editor);
        await Save(rig, editor);

        var ids = rig.Calls("AddAsync").Select(call => ((GameDefinition)call.Args[0]!).Id).ToList();
        Assert.Equal(2, ids.Count);
        Assert.Equal(ids[0], ids[1]);
    }

    [Fact]
    public async Task A_failed_save_keeps_the_editor_open()
    {
        var rig = EditorRig.Create();
        var editor = Filled(rig);
        rig.Backend.FailNext<IGamesApi>("AddAsync", new Savepoint.Core.Errors.ErrorInfo("error.unknown", new Dictionary<string, object?>(), string.Empty));

        await Save(rig, editor);

        Assert.Empty(rig.Shell.Calls);
        Assert.False(editor.IsSaving);
    }
}
