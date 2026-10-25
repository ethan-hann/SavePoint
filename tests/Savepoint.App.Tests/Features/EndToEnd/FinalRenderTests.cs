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
public class FinalRenderTests
{
    private const string _area = "final";
    private const string _skyrim = SampleScenario.SkyrimId;

    private static void AssertRendered(string path) =>
        Assert.True(new FileInfo(path).Length > 10_000, "The render looks empty.");

    private static ProfileRowViewModel Row(ComposedApp app, string id) => app.Games.Profiles.Single(row => row.Id == id);

    [AvaloniaFact]
    public async Task Shell_sidebar_3a()
    {
        using var app = await ComposedApp.StartWithViewAsync();

        AssertRendered(app.Render(_area, "shell-sidebar"));
    }

    [AvaloniaFact]
    public async Task Shell_library_3b()
    {
        using var app = await ComposedApp.StartWithViewAsync(layout: ShellLayout.Library);

        AssertRendered(app.Render(_area, "shell-library"));
    }

    [AvaloniaFact]
    public async Task Settings_layout_choice_3c()
    {
        using var app = await ComposedApp.StartWithViewAsync();
        app.Shell.Navigate(NavDestination.Settings);

        AssertRendered(app.Render(_area, "settings-layout-choice"));
    }

    [AvaloniaFact]
    public async Task Launch_with_nothing_to_change_2a()
    {
        using var app = await ComposedApp.StartWithViewAsync();
        await Row(app, SampleScenario.ModdedId).PlayCommand.ExecuteAsync(null).Soon();

        AssertRendered(app.Render(_area, "launch-nothing-to-change"));
    }

    [AvaloniaFact]
    public async Task Launch_dialog_2b()
    {
        using var app = await ComposedApp.StartWithViewAsync();
        _ = Row(app, SampleScenario.SurvivalId).PlayCommand.ExecuteAsync(null);

        AssertRendered(app.Render(_area, "launch-dialog"));
    }

    [AvaloniaFact]
    public async Task Progress_and_running_2c()
    {
        using var app = await ComposedApp.StartWithViewAsync();
        var play = Row(app, SampleScenario.VanillaId).PlayCommand.ExecuteAsync(null);
        app.Dialog<LaunchDialogViewModel>().SwitchCommand.Execute(null);
        await play.Soon();
        app.Backend.Launch.ReportSwitching(_skyrim, SampleScenario.SwitchProgress);
        AssertRendered(app.Render(_area, "progress"));

        app.Backend.Launch.ReportRunning(_skyrim, SampleScenario.RunningSince);
        AssertRendered(app.Render(_area, "running"));
    }

    [AvaloniaFact]
    public async Task Post_play_capture_2d()
    {
        using var app = await ComposedApp.StartWithViewAsync();
        await Row(app, SampleScenario.ModdedId).PlayCommand.ExecuteAsync(null).Soon();
        app.Backend.Launch.ReportRunning(_skyrim, SampleScenario.RunningSince);
        app.Backend.Launch.ReportExited(_skyrim, SampleScenario.PlayDuration, SampleScenario.NewSaveFiles);

        AssertRendered(app.Render(_area, "post-play-capture"));
    }

    [AvaloniaFact]
    public async Task First_run_2e()
    {
        using var app = await ComposedApp.StartWithViewAsync(new FakeBackend(ScenarioBuilder.Empty().WithSettings(SampleScenario.Settings)));

        AssertRendered(app.Render(_area, "first-run"));
    }

    [AvaloniaFact]
    public async Task New_profile_wizard_2f()
    {
        using var app = await ComposedApp.StartWithViewAsync();
        _ = app.Games.NewProfileCommand.ExecuteAsync(null);

        AssertRendered(app.Render(_area, "new-profile-wizard"));
    }

    [AvaloniaFact]
    public async Task Game_editor_2g()
    {
        using var app = await ComposedApp.StartWithViewAsync();
        app.Games.EditGameCommand.Execute(null);

        AssertRendered(app.Render(_area, "game-editor"));
    }

    [AvaloniaFact]
    public async Task History_list_2h()
    {
        using var app = await ComposedApp.StartWithViewAsync();
        app.Shell.Navigate(NavDestination.History);
        app.History.SelectedViewOption = app.History.ViewOptions[1];

        AssertRendered(app.Render(_area, "history-list"));
    }

    [AvaloniaFact]
    public async Task History_cards_2j()
    {
        using var app = await ComposedApp.StartWithViewAsync();
        app.Shell.Navigate(NavDestination.History);

        AssertRendered(app.Render(_area, "history-cards"));
    }

    [AvaloniaFact]
    public async Task Settings_light_2k()
    {
        using var app = await ComposedApp.StartWithViewAsync(theme: ThemePreference.Light);
        app.Shell.Navigate(NavDestination.Settings);

        AssertRendered(app.Render(_area, "settings-light"));
    }

    // The four error card states, each as the app shows it: a file in use and a folder gone on the Games screen, half a save
    // after a session, and a folder that is too broad in the editor; then the four cards together as the design draws them.
    [AvaloniaFact]
    public async Task Error_states_2l()
    {
        using var app = await ComposedApp.StartWithViewAsync();
        app.Backend.FailNext<ILaunchApi>(nameof(ILaunchApi.GetPreviewAsync), SampleScenario.FileInUseError);
        await Row(app, SampleScenario.ModdedId).PlayCommand.ExecuteAsync(null).Soon();
        var fileInUse = Assert.IsType<ErrorCardViewModel>(app.Games.InlineContent);
        AssertRendered(app.Render(_area, "file-in-use"));

        fileInUse.Actions.Single(action => action.Label == "Dismiss").Command.Execute(null);
        app.Backend.FailNext<ILaunchApi>(nameof(ILaunchApi.GetPreviewAsync), SampleScenario.FolderGoneError);
        await Row(app, SampleScenario.ModdedId).PlayCommand.ExecuteAsync(null).Soon();
        var folderGone = Assert.IsType<ErrorCardViewModel>(app.Games.InlineContent);
        AssertRendered(app.Render(_area, "folder-gone"));

        folderGone.Actions.Single(action => action.Label == "Dismiss").Command.Execute(null);
        await Row(app, SampleScenario.ModdedId).PlayCommand.ExecuteAsync(null).Soon();
        app.Backend.Launch.ReportRunning(_skyrim, SampleScenario.RunningSince);
        app.Backend.Launch.ReportExited(_skyrim, SampleScenario.PlayDuration, SampleScenario.NewSaveFiles);
        await app.Dialog<PostPlayDialogViewModel>().SaveCommand.ExecuteAsync(null).Soon();
        app.Backend.Jobs.Complete(app.Backend.Jobs.LastStarted(JobKind.Capture).JobId, SampleScenario.HalfSaved);
        var halfSaved = Assert.IsType<ErrorCardViewModel>(app.Games.InlineContent);
        AssertRendered(app.Render(_area, "half-saved"));

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
        AssertRendered(ThemeTestSupport.SaveFrame(gallery.Window, Path.Combine(ComposedApp.RenderFolder(_area), "error-states.png")));
    }
}
