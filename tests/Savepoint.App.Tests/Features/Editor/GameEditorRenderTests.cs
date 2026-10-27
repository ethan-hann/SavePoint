using Avalonia.Headless.XUnit;
using Savepoint.App.Features.Editor;
using Savepoint.App.Tests.Fakes;
using Savepoint.Core.Api;
using Savepoint.Core.Catalog;

namespace Savepoint.App.Tests.Features.Editor;

// Writes PNGs to render-output/editor/ (gitignored) at 1040x660 to compare with the previous render of the game editor and error card after a change.
public class GameEditorRenderTests
{
    private static void AssertRendered(string path) =>
        Assert.True(new FileInfo(path).Length > 10_000, "The render looks empty.");

    private static FakeBackend Sample(ThemePreference theme) =>
        new(ScenarioBuilder.Sample().WithSettings(SampleScenario.Settings with { Theme = theme }));

    [AvaloniaFact]
    public async Task Edit_mode_dark_renders_to_a_png()
    {
        using var harness = await EditorViewHarness.StartAsync();
        harness.Edit();

        AssertRendered(harness.Render("game-editor-edit-dark"));
    }

    // The pane is taller than the 660px frame, so this one shows all of it, down to the folders that hold mods.
    [AvaloniaFact]
    public async Task Edit_mode_dark_with_the_whole_pane_in_view_renders_to_a_png()
    {
        using var harness = await EditorViewHarness.StartAsync(height: 900);
        harness.Edit();

        AssertRendered(harness.Render("game-editor-edit-dark-tall"));
    }

    [AvaloniaFact]
    public async Task Edit_mode_light_renders_to_a_png()
    {
        using var harness = await EditorViewHarness.StartAsync(Sample(ThemePreference.Light), ThemePreference.Light);
        harness.Edit();

        AssertRendered(harness.Render("game-editor-edit-light"));
    }

    [AvaloniaFact]
    public async Task Create_mode_empty_dark_renders_to_a_png()
    {
        using var harness = await EditorViewHarness.StartAsync();
        harness.New();

        AssertRendered(harness.Render("game-editor-create-dark"));
    }

    [AvaloniaFact]
    public async Task The_Steam_choice_with_problems_dark_renders_to_a_png()
    {
        using var harness = await EditorViewHarness.StartAsync();
        var editor = harness.New();
        editor.Name = "Hollow Knight";
        editor.InstallPath = @"C:\Games\Hollow Knight";
        editor.LaunchChoice = 1;
        editor.Uri = "not a link";
        editor.SavesPath = @"{appData}\Hollow Knight";
        _ = editor.SaveCommand.ExecuteAsync(null);
        harness.Settle();

        AssertRendered(harness.Render("game-editor-steam-problems-dark"));
    }

    [AvaloniaFact]
    public async Task A_folder_too_broad_to_track_dark_renders_to_a_png()
    {
        using var harness = await EditorViewHarness.StartAsync();
        var editor = harness.New();
        editor.Name = "Skyrim Special Edition";
        editor.InstallPath = @"C:\Program Files (x86)";
        editor.ExePath = "SkyrimSELauncher.exe";
        harness.Backend.FailNext<IGamesApi>("AddAsync", SampleScenario.FolderTooBroadError);

        var saving = editor.SaveCommand.ExecuteAsync(null);
        harness.Settle();
        Assert.IsType<ConsentDialogViewModel>(harness.Dialogs.Current).ConfirmCommand.Execute(null);
        await saving;

        AssertRendered(harness.Render("game-editor-too-broad-dark"));
    }

    [AvaloniaFact]
    public async Task A_folder_that_is_gone_dark_renders_to_a_png()
    {
        var caretaker = SampleScenario.Caretaker;
        var backend = new FakeBackend(ScenarioBuilder.Sample().WithFolderStatus(
            caretaker.Id,
            new FolderStatus(RepoRole.Install, caretaker.Install.Path, caretaker.Install.Path, false, new ConsentStatus(true, ScenarioBuilder.DefaultNow)),
            new FolderStatus(RepoRole.External, caretaker.External!.Path, @"C:\Users\you\AppData\Local\TheLastCaretaker\Saved\SaveGames", true, new ConsentStatus(true, ScenarioBuilder.DefaultNow))));
        using var harness = await EditorViewHarness.StartAsync(backend);
        harness.Edit(caretaker.Id);

        AssertRendered(harness.Render("game-editor-folder-gone-dark"));
    }

    [AvaloniaFact]
    public async Task The_folder_confirmation_dark_renders_to_a_png()
    {
        using var harness = await EditorViewHarness.StartAsync();
        var editor = harness.New(harness.Backend.Games.Templates[0].ToDraft());
        _ = editor.SaveCommand.ExecuteAsync(null);

        AssertRendered(harness.Render("consent-confirmation-dark"));
    }

    [AvaloniaFact]
    public async Task The_reconnect_question_dark_renders_to_a_png()
    {
        var history = new OrphanedHistory(
            "hollow-knight-old1",
            [RepoRole.Install, RepoRole.External],
            [new RecoverableProfile("profile/modded-run-ab12", "Modded run"), new RecoverableProfile("profile/xy99", null)]);
        var backend = new FakeBackend(ScenarioBuilder.Sample().WithOrphanedHistory(@"C:\Games\Hollow Knight", history));
        using var harness = await EditorViewHarness.StartAsync(backend);
        var editor = harness.New();
        editor.Name = "Hollow Knight";
        editor.InstallPath = @"C:\Games\Hollow Knight";
        editor.ExePath = "hollow_knight.exe";
        _ = editor.SaveCommand.ExecuteAsync(null);
        harness.Settle();
        Assert.IsType<ConsentDialogViewModel>(harness.Dialogs.Current).ConfirmCommand.Execute(null);
        await EditorRig.Eventually(() => harness.Dialogs.Current as ReconnectDialogViewModel);

        AssertRendered(harness.Render("reconnect-question-dark"));
    }
}
