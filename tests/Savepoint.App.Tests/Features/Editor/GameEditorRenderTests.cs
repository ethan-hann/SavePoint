using Avalonia.Headless.XUnit;
using Savepoint.App.Features.Editor;
using Savepoint.App.Tests.Fakes;
using Savepoint.Core.Api;
using Savepoint.Core.Catalog;

namespace Savepoint.App.Tests.Features.Editor;

// Writes PNGs to render-output/editor/ (gitignored) at 1040x660 to compare with the previous render of the game editor and error card after a change.
// Nothing here asserts on a picture, so the renders are only drawn on request: SAVEPOINT_RENDER=1.
public class GameEditorRenderTests
{
    private static FakeBackend Sample(ThemePreference theme) =>
        new(ScenarioBuilder.Sample().WithSettings(SampleScenario.Settings with { Theme = theme }));

    [AvaloniaFact]
    public async Task Writes_every_game_editor_render_on_request()
    {
        RenderOutput.SkipUnlessRequested();

        await EditModeDark();
        await EditModeDarkWithTheWholePaneInView();
        await EditModeLight();
        await CreateModeEmptyDark();
        await TheSteamChoiceWithProblemsDark();
        await AFolderTooBroadToTrackDark();
        await AFolderThatIsGoneDark();
        await TheFolderConfirmationDark();
        await TheReconnectQuestionDark();
    }

    private static async Task EditModeDark()
    {
        using var harness = await EditorViewHarness.StartAsync();
        harness.Edit();

        harness.Render("game-editor-edit-dark");
    }

    // The pane is taller than the 660px frame, so this one shows all of it, down to More settings.
    private static async Task EditModeDarkWithTheWholePaneInView()
    {
        using var harness = await EditorViewHarness.StartAsync(height: 2200);
        harness.Edit();

        harness.Render("game-editor-edit-dark-tall");
    }

    private static async Task EditModeLight()
    {
        using var harness = await EditorViewHarness.StartAsync(Sample(ThemePreference.Light), ThemePreference.Light);
        harness.Edit();

        harness.Render("game-editor-edit-light");
    }

    private static async Task CreateModeEmptyDark()
    {
        using var harness = await EditorViewHarness.StartAsync();
        harness.New();

        harness.Render("game-editor-create-dark");
    }

    private static async Task TheSteamChoiceWithProblemsDark()
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

        harness.Render("game-editor-steam-problems-dark");
    }

    private static async Task AFolderTooBroadToTrackDark()
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

        harness.Render("game-editor-too-broad-dark");
    }

    private static async Task AFolderThatIsGoneDark()
    {
        var caretaker = SampleScenario.Caretaker;
        var backend = new FakeBackend(ScenarioBuilder.Sample().WithFolderStatus(
            caretaker.Id,
            new FolderStatus(RepoRole.Install, caretaker.Install.Path, caretaker.Install.Path, false, new ConsentStatus(true, ScenarioBuilder.DefaultNow)),
            new FolderStatus(RepoRole.External, caretaker.External!.Path, @"C:\Users\you\AppData\Local\TheLastCaretaker\Saved\SaveGames", true, new ConsentStatus(true, ScenarioBuilder.DefaultNow))));
        using var harness = await EditorViewHarness.StartAsync(backend);
        harness.Edit(caretaker.Id);

        harness.Render("game-editor-folder-gone-dark");
    }

    private static async Task TheFolderConfirmationDark()
    {
        using var harness = await EditorViewHarness.StartAsync();
        var editor = harness.New(harness.Backend.Games.Templates[0].ToDraft());
        _ = editor.SaveCommand.ExecuteAsync(null);

        harness.Render("consent-confirmation-dark");
    }

    private static async Task TheReconnectQuestionDark()
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

        harness.Render("reconnect-question-dark");
    }
}
