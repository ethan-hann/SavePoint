using Avalonia.Headless.XUnit;
using Savepoint.App.Features.Settings;
using Savepoint.App.Tests.Fakes;
using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;

namespace Savepoint.App.Tests.Features.Settings;

// Writes PNGs to render-output/settings/ (gitignored) at 1040x660 to compare with the previous render of the Settings screen and layout choice after a change.
// Nothing here asserts on a picture, so the renders are only drawn on request: SAVEPOINT_RENDER=1.
public class SettingsRenderTests
{
    private static FakeBackend With(ThemePreference theme, ShellLayout layout = ShellLayout.Sidebar) =>
        new(ScenarioBuilder.Sample().WithSettings(SampleScenario.Settings with { Theme = theme, Layout = layout }));

    [AvaloniaFact]
    public async Task Writes_every_Settings_render_on_request()
    {
        RenderOutput.SkipUnlessRequested();

        await Light();
        await Dark();
        await TheEndOfThePageDark();
        await TheLayoutChoiceDark();
        await TheLayoutChoiceLightWithLibraryChosen();
        await TheClearAllDataConfirmationDark();
        await TheClearAllDataConfirmationLightWithTheWordTyped();
        await AMoveInProgressDark();
        await ProblemsAndLeftBehindFilesDark();
        await AFailedReadDark();
    }

    private static async Task Light()
    {
        using var harness = await SettingsScreenViewTests.StartAsync(With(ThemePreference.Light), ThemePreference.Light, notices: null);

        harness.Render("settings-light");
    }

    private static async Task Dark()
    {
        using var harness = await SettingsScreenViewTests.StartAsync();

        harness.Render("settings-dark");
    }

    private static async Task TheEndOfThePageDark()
    {
        using var harness = await SettingsScreenViewTests.StartAsync();
        harness.Find<Avalonia.Controls.ScrollViewer>("PageScroll").ScrollToEnd();

        harness.Render("settings-end-dark");
    }

    private static async Task TheLayoutChoiceDark()
    {
        using var harness = await SettingsScreenViewTests.StartAsync(height: 760);
        harness.Find<Avalonia.Controls.ScrollViewer>("PageScroll").Offset = new Avalonia.Vector(0, 190);

        harness.Render("settings-layout-choice-dark");
    }

    private static async Task TheLayoutChoiceLightWithLibraryChosen()
    {
        using var harness = await SettingsScreenViewTests.StartAsync(With(ThemePreference.Light, ShellLayout.Library), ThemePreference.Light, height: 760);
        harness.Find<Avalonia.Controls.ScrollViewer>("PageScroll").Offset = new Avalonia.Vector(0, 190);

        harness.Render("settings-layout-choice-light-library");
    }

    private static async Task TheClearAllDataConfirmationDark()
    {
        using var harness = await SettingsScreenViewTests.StartAsync(realDialogs: true, height: 760);
        var screen = (SettingsScreenViewModel)harness.Shell.CurrentScreen!;

        _ = screen.ClearAll.RemoveCommand.ExecuteAsync(null);
        harness.Settle();

        harness.Render("clear-all-data-confirmation-dark");
    }

    private static async Task TheClearAllDataConfirmationLightWithTheWordTyped()
    {
        using var harness = await SettingsScreenViewTests.StartAsync(With(ThemePreference.Light), ThemePreference.Light, realDialogs: true, height: 760);
        var screen = (SettingsScreenViewModel)harness.Shell.CurrentScreen!;

        _ = screen.ClearAll.RemoveCommand.ExecuteAsync(null);
        harness.Settle();
        ((ClearAllDataDialogViewModel)harness.Main.Dialogs.Current!).TypedText = "delete";

        harness.Render("clear-all-data-confirmation-light-typed");
    }

    private static async Task AMoveInProgressDark()
    {
        using var harness = await SettingsScreenViewTests.StartAsync();
        FakeAssert.Ok(await harness.Backend.Settings.RelocateStorageAsync(@"E:\NewData"));
        harness.Backend.Jobs.ReportProgress(
            harness.Backend.Jobs.LastStarted(JobKind.RelocateStorage).JobId,
            new JobProgress(BytesDone: 19_800_000_000, BytesTotal: 79_200_000_000, CurrentPath: @"Skyrim Special Edition\Data\textures\architecture\whiterun\wrbuilding05.dds"));

        harness.Render("settings-move-in-progress-dark");
    }

    private static async Task ProblemsAndLeftBehindFilesDark()
    {
        var blocked = new StorageRelocationPreview(SampleScenario.StorageRoot, @"E:\NewData", [], 0, 0, 0, ErrorInfo.From(new StorageDestinationNotEmptyException(@"E:\NewData")));
        using var harness = await SettingsScreenViewTests.StartAsync(new FakeBackend(ScenarioBuilder.Sample().WithRelocationPreview(blocked)), height: 900);
        harness.FakeDialogs.AnswerFolder(@"E:\NewData");
        var screen = (SettingsScreenViewModel)harness.Shell.CurrentScreen!;
        await screen.Storage.ChangeCommand.ExecuteAsync(null);

        harness.Render("settings-problem-dark");
    }

    private static async Task AFailedReadDark()
    {
        using var harness = await SettingsScreenViewTests.StartAsync();
        harness.Backend.FailNext<ISettingsApi>(nameof(ISettingsApi.GetAsync), ErrorInfo.From(new InvalidOperationException("No settings.")));
        harness.Shell.Navigate(Savepoint.App.Features.Shell.NavDestination.Games);
        harness.Shell.Navigate(Savepoint.App.Features.Shell.NavDestination.Settings);

        harness.Render("settings-failed-read-dark");
    }
}
