using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Savepoint.App.Composition;
using Savepoint.App.Features.Common.Toasts;
using Savepoint.App.Features.Settings;
using Savepoint.App.Features.Shell;
using Savepoint.App.Tests.Fakes;
using Savepoint.App.Tests.Features.History;

namespace Savepoint.App.Tests.Features.Settings;

public class AboutLogFolderTests
{
    private const string _logFolder = @"C:\Users\Sam\AppData\Local\SavePoint\Logs";

    [Fact]
    public async Task The_about_section_opens_the_log_folder_in_the_file_manager()
    {
        var rig = await SettingsRig.ShownAsync(logFolder: _logFolder);

        Assert.True(rig.Screen.About.HasLogFolder);
        rig.Screen.About.OpenLogFolderCommand.Execute(null);

        Assert.Equal([_logFolder], rig.Revealer.Revealed);
        Assert.Empty(rig.Toasts.Toasts);
    }

    [Fact]
    public async Task Without_a_log_folder_the_action_is_hidden_and_does_nothing()
    {
        var rig = await SettingsRig.ShownAsync();

        Assert.False(rig.Screen.About.HasLogFolder);
        Assert.False(rig.Screen.About.OpenLogFolderCommand.CanExecute(null));
        rig.Screen.About.OpenLogFolderCommand.Execute(null);
        Assert.Empty(rig.Revealer.Revealed);
    }

    [Fact]
    public async Task A_file_manager_that_will_not_open_is_reported_in_words()
    {
        var rig = await SettingsRig.ShownAsync(logFolder: _logFolder);
        rig.Revealer.Succeeds = false;

        rig.Screen.About.OpenLogFolderCommand.Execute(null);

        var toast = Assert.Single(rig.Toasts.Toasts);
        Assert.Equal(ToastKind.Error, toast.Message.Kind);
        Assert.Equal("SavePoint couldn't open the log folder.", toast.Message.Text);
    }

    [AvaloniaFact]
    public async Task The_button_shows_after_the_notices_button_when_there_is_a_folder()
    {
        using var harness = await StartAsync(_logFolder);

        var button = harness.Find<Button>("LogFolderButton");

        Assert.True(button.IsEffectivelyVisible);
        Assert.Equal("Open the log folder", button.Content as string);
        Assert.True(harness.Find<Button>("NoticesButton").Bounds.X < button.Bounds.X);
    }

    [AvaloniaFact]
    public async Task The_button_is_hidden_when_there_is_no_folder()
    {
        using var harness = await StartAsync(null);

        Assert.False(harness.Find<Button>("LogFolderButton").IsEffectivelyVisible);
    }

    [Fact]
    public void The_running_apps_settings_take_the_log_folder_and_the_file_manager_from_the_services()
    {
        var backend = FakeBackend.Sample();
        var revealer = new RecordingFileRevealer();
        var services = new AppServices(backend.Games, backend.Profiles, backend.Launch, backend.Settings, backend.History, backend.Jobs, backend.AppStatus, backend.Artwork)
        {
            FileRevealer = revealer,
            LogFolder = _logFolder,
        };

        var environment = FeatureComposition.SettingsEnvironmentOf(services);

        Assert.Equal(_logFolder, environment.LogFolder);
        Assert.Same(revealer, environment.Revealer);
    }

    private static Task<ScreenHarness> StartAsync(string? logFolder) =>
        ScreenHarness.StartAsync(
            "settings",
            context => [new SettingsScreenFactory(context, new SettingsEnvironment(new RecordingFileRevealer(), () => @"C:\SavePoint\THIRD-PARTY-NOTICES.md", "1.2.3", logFolder))],
            NavDestination.Settings,
            FakeBackend.Sample(),
            Savepoint.Core.Catalog.ThemePreference.Dark,
            1040,
            660,
            false);
}
