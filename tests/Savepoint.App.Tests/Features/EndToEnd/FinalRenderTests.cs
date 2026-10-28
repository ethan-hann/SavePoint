using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Savepoint.App.Features.Common.Errors;
using Savepoint.App.Features.Games;
using Savepoint.App.Features.Launch;
using Savepoint.App.Features.Shell;
using Savepoint.App.Tests.Fakes;
using Savepoint.App.Tests.Features.Games;
using Savepoint.App.Tests.Theme;
using Savepoint.Core.Api;
using Savepoint.Core.Catalog;

namespace Savepoint.App.Tests.Features.EndToEnd;

// Every screen of the design (both shell layouts, the launch flow, the wizard, editor, History, Settings, the error cards and the
// first run) as the finished app draws it, from the composed app at the design's 1040x660 frame, in render-output/final/ to be set beside docs/ui/screenshots.
// Nothing here asserts on a picture, so the renders are only drawn on request: SAVEPOINT_RENDER=1.
public class FinalRenderTests
{
    private const string _area = "final";
    private const string _skyrim = SampleScenario.SkyrimId;

    private static ProfileRowViewModel Row(ComposedApp app, string id) => app.Games.Profiles.Single(row => row.Id == id);

    [AvaloniaFact]
    public async Task Writes_every_screen_of_the_design_on_request()
    {
        RenderOutput.SkipUnlessRequested();

        await ShellSidebar();
        await ShellLibrary();
        await SettingsLayoutChoice();
        await LaunchWithNothingToChange();
        await LaunchDialog();
        await ProgressAndRunning();
        await PostPlayCapture();
        await FirstRun();
        await NewProfileWizard();
        await GameEditor();
        await HistoryList();
        await HistoryCards();
        await SettingsLight();
        await ErrorStates();
    }

    private static async Task ShellSidebar()
    {
        using var app = await ComposedApp.StartWithViewAsync();

        app.Render(_area, "shell-sidebar");
    }

    private static async Task ShellLibrary()
    {
        using var app = await ComposedApp.StartWithViewAsync(layout: ShellLayout.Library);

        app.Render(_area, "shell-library");
    }

    private static async Task SettingsLayoutChoice()
    {
        using var app = await ComposedApp.StartWithViewAsync();
        app.Shell.Navigate(NavDestination.Settings);

        app.Render(_area, "settings-layout-choice");
    }

    private static async Task LaunchWithNothingToChange()
    {
        using var app = await ComposedApp.StartWithViewAsync();
        await Row(app, SampleScenario.ModdedId).PlayCommand.ExecuteAsync(null).Soon();

        app.Render(_area, "launch-nothing-to-change");
    }

    private static async Task LaunchDialog()
    {
        using var app = await ComposedApp.StartWithViewAsync();
        _ = Row(app, SampleScenario.SurvivalId).PlayCommand.ExecuteAsync(null);

        app.Render(_area, "launch-dialog");
    }

    private static async Task ProgressAndRunning()
    {
        using var app = await ComposedApp.StartWithViewAsync();
        var play = Row(app, SampleScenario.VanillaId).PlayCommand.ExecuteAsync(null);
        app.Dialog<LaunchDialogViewModel>().SwitchCommand.Execute(null);
        await play.Soon();
        app.Backend.Launch.ReportSwitching(_skyrim, SampleScenario.SwitchProgress);
        app.Render(_area, "progress");

        app.Backend.Launch.ReportRunning(_skyrim, SampleScenario.RunningSince);
        app.Render(_area, "running");
    }

    private static async Task PostPlayCapture()
    {
        using var app = await ComposedApp.StartWithViewAsync();
        await Row(app, SampleScenario.ModdedId).PlayCommand.ExecuteAsync(null).Soon();
        app.Backend.Launch.ReportRunning(_skyrim, SampleScenario.RunningSince);
        app.Backend.Launch.ReportExited(_skyrim, SampleScenario.PlayDuration, SampleScenario.NewSaveFiles);

        app.Render(_area, "post-play-capture");
    }

    private static async Task FirstRun()
    {
        using var app = await ComposedApp.StartWithViewAsync(new FakeBackend(ScenarioBuilder.Empty().WithSettings(SampleScenario.Settings)));

        app.Render(_area, "first-run");
    }

    private static async Task NewProfileWizard()
    {
        using var app = await ComposedApp.StartWithViewAsync();
        _ = app.Games.NewProfileCommand.ExecuteAsync(null);

        app.Render(_area, "new-profile-wizard");
    }

    private static async Task GameEditor()
    {
        using var app = await ComposedApp.StartWithViewAsync();
        app.Games.EditGameCommand.Execute(null);

        app.Render(_area, "game-editor");
    }

    private static async Task HistoryList()
    {
        using var app = await ComposedApp.StartWithViewAsync();
        app.Shell.Navigate(NavDestination.History);
        app.History.SelectedViewOption = app.History.ViewOptions[1];

        app.Render(_area, "history-list");
    }

    private static async Task HistoryCards()
    {
        using var app = await ComposedApp.StartWithViewAsync();
        app.Shell.Navigate(NavDestination.History);

        app.Render(_area, "history-cards");
    }

    private static async Task SettingsLight()
    {
        using var app = await ComposedApp.StartWithViewAsync(theme: ThemePreference.Light);
        app.Shell.Navigate(NavDestination.Settings);

        app.Render(_area, "settings-light");
    }

    // The four error card states, each as the app shows it: a file in use and a folder gone on the Games screen, half a save
    // after a session, and a folder that is too broad in the editor; then the four cards together as the design draws them.
    private static async Task ErrorStates()
    {
        using var app = await ComposedApp.StartWithViewAsync();
        app.Backend.FailNext<ILaunchApi>(nameof(ILaunchApi.GetPreviewAsync), SampleScenario.FileInUseError);
        await Row(app, SampleScenario.ModdedId).PlayCommand.ExecuteAsync(null).Soon();
        var fileInUse = Assert.IsType<ErrorCardViewModel>(app.Games.InlineContent);
        app.Render(_area, "file-in-use");

        fileInUse.Actions.Single(action => action.Label == "Dismiss").Command.Execute(null);
        app.Backend.FailNext<ILaunchApi>(nameof(ILaunchApi.GetPreviewAsync), SampleScenario.FolderGoneError);
        await Row(app, SampleScenario.ModdedId).PlayCommand.ExecuteAsync(null).Soon();
        var folderGone = Assert.IsType<ErrorCardViewModel>(app.Games.InlineContent);
        app.Render(_area, "folder-gone");

        folderGone.Actions.Single(action => action.Label == "Dismiss").Command.Execute(null);
        await Row(app, SampleScenario.ModdedId).PlayCommand.ExecuteAsync(null).Soon();
        app.Backend.Launch.ReportRunning(_skyrim, SampleScenario.RunningSince);
        app.Backend.Launch.ReportExited(_skyrim, SampleScenario.PlayDuration, SampleScenario.NewSaveFiles);
        await app.Dialog<PostPlayDialogViewModel>().SaveCommand.ExecuteAsync(null).Soon();
        app.Backend.Jobs.Complete(app.Backend.Jobs.LastStarted(JobKind.Capture).JobId, SampleScenario.HalfSaved);
        var halfSaved = Assert.IsType<ErrorCardViewModel>(app.Games.InlineContent);
        app.Render(_area, "half-saved");

        var tooBroad = new ErrorCardViewModel(SampleScenario.FolderTooBroadError, app.Text, []);
        var grid = new Grid { Width = 1040, Height = 660, RowDefinitions = new RowDefinitions("*,*"), ColumnDefinitions = new ColumnDefinitions("*,*") };
        var cards = new object[] { fileInUse, tooBroad, halfSaved, folderGone };
        for (var i = 0; i < cards.Length; i++)
        {
            var host = new ContentControl { Content = cards[i], Margin = new Avalonia.Thickness(27, 22, 27, 6) };
            Grid.SetRow(host, i / 2);
            Grid.SetColumn(host, i % 2);
            grid.Children.Add(host);
        }

        using var gallery = new ThemeHost(grid, ThemePreference.Dark, width: 1040, height: 660);
        ThemeTestSupport.SaveFrame(gallery.Window, Path.Combine(ComposedApp.RenderFolder(_area), "error-states.png"));
    }
}
