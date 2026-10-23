using Avalonia.Headless.XUnit;
using Savepoint.App.Features.Settings;
using Savepoint.App.Tests.Fakes;
using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;

namespace Savepoint.App.Tests.Features.Settings;

// Writes PNGs to render-output/settings/ (gitignored) for side-by-side review against the Settings screen and layout choice designs at 1040x660.
public class SettingsRenderTests
{
    private static void AssertRendered(string path) =>
        Assert.True(new FileInfo(path).Length > 10_000, "The render looks empty.");

    private static FakeBackend With(ThemePreference theme, ShellLayout layout = ShellLayout.Sidebar) =>
        new(ScenarioBuilder.Mockup().WithSettings(MockupScenario.Settings with { Theme = theme, Layout = layout }));

    [AvaloniaFact]
    public async Task Light_renders_to_a_png()
    {
        using var harness = await SettingsScreenViewTests.StartAsync(With(ThemePreference.Light), ThemePreference.Light, notices: null);

        AssertRendered(harness.Render("2k-settings-light"));
    }

    [AvaloniaFact]
    public async Task Dark_renders_to_a_png()
    {
        using var harness = await SettingsScreenViewTests.StartAsync();

        AssertRendered(harness.Render("2k-settings-dark"));
    }

    [AvaloniaFact]
    public async Task The_end_of_the_page_dark_renders_to_a_png()
    {
        using var harness = await SettingsScreenViewTests.StartAsync();
        harness.Find<Avalonia.Controls.ScrollViewer>("PageScroll").ScrollToEnd();

        AssertRendered(harness.Render("settings-end-dark"));
    }

    [AvaloniaFact]
    public async Task The_layout_choice_dark_renders_to_a_png()
    {
        using var harness = await SettingsScreenViewTests.StartAsync(height: 760);
        harness.Find<Avalonia.Controls.ScrollViewer>("PageScroll").Offset = new Avalonia.Vector(0, 190);

        AssertRendered(harness.Render("3c-settings-layout-choice-dark"));
    }

    [AvaloniaFact]
    public async Task The_layout_choice_light_with_library_chosen_renders_to_a_png()
    {
        using var harness = await SettingsScreenViewTests.StartAsync(With(ThemePreference.Light, ShellLayout.Library), ThemePreference.Light, height: 760);
        harness.Find<Avalonia.Controls.ScrollViewer>("PageScroll").Offset = new Avalonia.Vector(0, 190);

        AssertRendered(harness.Render("3c-settings-layout-choice-light-library"));
    }

    [AvaloniaFact]
    public async Task The_clear_all_data_confirmation_dark_renders_to_a_png()
    {
        using var harness = await SettingsScreenViewTests.StartAsync(realDialogs: true, height: 760);
        var screen = (SettingsScreenViewModel)harness.Shell.CurrentScreen!;

        _ = screen.ClearAll.RemoveCommand.ExecuteAsync(null);
        harness.Settle();

        AssertRendered(harness.Render("clear-all-data-confirmation-dark"));
    }

    [AvaloniaFact]
    public async Task The_clear_all_data_confirmation_light_with_the_word_typed_renders_to_a_png()
    {
        using var harness = await SettingsScreenViewTests.StartAsync(With(ThemePreference.Light), ThemePreference.Light, realDialogs: true, height: 760);
        var screen = (SettingsScreenViewModel)harness.Shell.CurrentScreen!;

        _ = screen.ClearAll.RemoveCommand.ExecuteAsync(null);
        harness.Settle();
        ((ClearAllDataDialogViewModel)harness.Main.Dialogs.Current!).TypedText = "delete";

        AssertRendered(harness.Render("clear-all-data-confirmation-light-typed"));
    }

    [AvaloniaFact]
    public async Task A_move_in_progress_dark_renders_to_a_png()
    {
        using var harness = await SettingsScreenViewTests.StartAsync();
        FakeAssert.Ok(await harness.Backend.Settings.RelocateStorageAsync(@"E:\NewData"));
        harness.Backend.Jobs.ReportProgress(
            harness.Backend.Jobs.LastStarted(JobKind.RelocateStorage).JobId,
            new JobProgress(BytesDone: 19_800_000_000, BytesTotal: 79_200_000_000, CurrentPath: @"Skyrim Special Edition\Data\textures\architecture\whiterun\wrbuilding05.dds"));

        AssertRendered(harness.Render("settings-move-in-progress-dark"));
    }

    [AvaloniaFact]
    public async Task Problems_and_left_behind_files_dark_render_to_a_png()
    {
        var blocked = new StorageRelocationPreview(MockupScenario.StorageRoot, @"E:\NewData", [], 0, 0, 0, ErrorInfo.From(new StorageDestinationNotEmptyException(@"E:\NewData")));
        using var harness = await SettingsScreenViewTests.StartAsync(new FakeBackend(ScenarioBuilder.Mockup().WithRelocationPreview(blocked)), height: 900);
        harness.FakeDialogs.AnswerFolder(@"E:\NewData");
        var screen = (SettingsScreenViewModel)harness.Shell.CurrentScreen!;
        await screen.Storage.ChangeCommand.ExecuteAsync(null);

        AssertRendered(harness.Render("settings-problem-dark"));
    }

    [AvaloniaFact]
    public async Task A_failed_read_dark_renders_to_a_png()
    {
        using var harness = await SettingsScreenViewTests.StartAsync();
        harness.Backend.FailNext<ISettingsApi>(nameof(ISettingsApi.GetAsync), ErrorInfo.From(new InvalidOperationException("No settings.")));
        harness.Shell.Navigate(Savepoint.App.Features.Shell.NavDestination.Games);
        harness.Shell.Navigate(Savepoint.App.Features.Shell.NavDestination.Settings);

        AssertRendered(harness.Render("settings-failed-read-dark"));
    }
}
