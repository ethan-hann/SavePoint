using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Savepoint.App.Features.Common;
using Savepoint.App.Features.Common.Formatting;
using Savepoint.App.Features.Shell;
using Savepoint.App.Hosting;
using Savepoint.App.Localization;
using Savepoint.App.Tests.Fakes;
using Savepoint.App.Tests.Features.Settings;
using Savepoint.Core.Errors;

namespace Savepoint.App.Tests.Features.Shell;

public class StartupLogFolderTests
{
    private const string _logFolder = @"C:\Users\Sam\AppData\Local\SavePoint\Logs";

    private static readonly UiFormatter _text = new(LocalizationCatalog.LoadEmbedded("en", throwOnMissingKey: true), os: "windows", timeZone: TimeZoneInfo.Utc);

    private static ErrorInfo Failure => ErrorInfo.From(new IOException("The configuration folder cannot be created."));

    private static async Task<StartupViewModel> FailedAsync(string? logFolder, RecordingFileRevealer? revealer = null)
    {
        var startup = new StartupViewModel(AppBootstrap.Unavailable(Failure, logFolder).AppStatus, _text, canTryAgain: false, logFolder, revealer);
        await startup.CheckAsync();
        return startup;
    }

    [Fact]
    public async Task A_backend_that_never_started_shows_the_log_folder_and_offers_to_open_it()
    {
        var revealer = new RecordingFileRevealer();
        var startup = await FailedAsync(_logFolder, revealer);

        Assert.Equal(_logFolder, startup.Card!.Path);
        var action = Assert.Single(startup.Card.Actions);
        Assert.Equal("Open the log folder", action.Label);
        Assert.Contains("send the newest file from the log folder", startup.Card.Body, StringComparison.Ordinal);
        Assert.Contains("Close SavePoint and open it again", startup.Card.Body, StringComparison.Ordinal);

        action.Command.Execute(null);

        Assert.Equal([_logFolder], revealer.Revealed);
        Assert.False(startup.HasLogFolderError);
    }

    [Fact]
    public async Task A_file_manager_that_will_not_open_is_reported_under_the_card()
    {
        var revealer = new RecordingFileRevealer { Succeeds = false };
        var startup = await FailedAsync(_logFolder, revealer);

        Assert.Single(startup.Card!.Actions).Command.Execute(null);

        Assert.True(startup.HasLogFolderError);
        Assert.Equal("SavePoint couldn't open the log folder. You can find it at the path above.", startup.LogFolderError);
    }

    [Fact]
    public async Task Without_a_log_folder_the_card_is_as_it_was()
    {
        var startup = await FailedAsync(null);

        Assert.Null(startup.Card!.Path);
        Assert.Empty(startup.Card.Actions);
        Assert.DoesNotContain("log folder", startup.Card.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_check_that_can_be_tried_again_does_not_talk_about_the_log()
    {
        var startup = new StartupViewModel(AppBootstrap.Unavailable(Failure, _logFolder).AppStatus, _text, canTryAgain: true, _logFolder, new RecordingFileRevealer());

        await startup.CheckAsync();

        Assert.Null(startup.Card!.Path);
        Assert.Equal(["Try again"], startup.Card.Actions.Select(action => action.Label));
    }

    [Fact]
    public async Task The_composition_hands_the_services_log_folder_and_file_manager_to_the_screen()
    {
        var revealer = new RecordingFileRevealer();
        var services = AppBootstrap.Unavailable(Failure, _logFolder) with { FileRevealer = revealer };
        var main = AppComposition.Create(services, _text, new RecordingThemeApplier(), () => null, new ImmediateUiDispatcher(), new ManualTimeProvider(), new FakeDialogs());

        await main.Startup.CheckAsync();

        Assert.Equal(_logFolder, main.Startup.Card!.Path);
        Assert.Single(main.Startup.Card.Actions).Command.Execute(null);
        Assert.Equal([_logFolder], revealer.Revealed);
    }

    [AvaloniaFact]
    public async Task The_error_under_the_card_shows_only_when_there_is_one()
    {
        var revealer = new RecordingFileRevealer { Succeeds = false };
        var startup = await FailedAsync(_logFolder, revealer);
        var view = new StartupView { DataContext = startup };
        var window = new Window { Content = view };
        window.Show();
        var error = view.FindControl<TextBlock>("LogFolderError")!;

        Assert.False(error.IsEffectivelyVisible);
        Assert.Single(startup.Card!.Actions).Command.Execute(null);

        Assert.True(error.IsEffectivelyVisible);
        Assert.Contains("couldn't open the log folder", error.Text, StringComparison.Ordinal);
        window.Close();
    }
}
