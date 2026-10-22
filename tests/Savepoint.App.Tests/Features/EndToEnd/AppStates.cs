using Savepoint.App.Features.Common.Toasts;
using Savepoint.App.Features.Editor;
using Savepoint.App.Features.Games;
using Savepoint.App.Features.History;
using Savepoint.App.Features.Launch;
using Savepoint.App.Features.Settings;
using Savepoint.App.Features.Shell;
using Savepoint.App.Tests.Fakes;
using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;

namespace Savepoint.App.Tests.Features.EndToEnd;

// Every screen and dialog of the app in the states a person meets them, reached by using the real app.
internal static class AppStates
{
    private const string _skyrim = MockupScenario.SkyrimId;

    public static TheoryData<string> All =>
    [
        "games-sidebar",
        "games-library",
        "games-banner",
        "games-banner-details",
        "games-running",
        "games-checking",
        "games-failure-card",
        "games-delete-game",
        "games-delete-profile",
        "games-edit-profile",
        "games-duplicate-profile",
        "games-new-profile-wizard",
        "launch-dialog",
        "launch-dialog-unsaved",
        "launch-dialog-keep-as-new",
        "launch-progress",
        "launch-post-play",
        "launch-post-play-older-point",
        "history-cards",
        "history-list",
        "history-filtered-empty",
        "history-empty",
        "settings",
        "settings-clear-all",
        "first-run",
        "first-run-templates",
        "editor-existing",
        "editor-new",
        "editor-consent",
        "editor-reconnect",
        "startup-git-unavailable",
        "startup-corrupt-catalog",
        "toast",
        "job-pill",
    ];


    // The states that put a dialog on top; the progress dialog is left out because it can only be cancelled, not dismissed.
    public static TheoryData<string> Dialogs =>
    [
        "games-banner-details",
        "games-delete-game",
        "games-delete-profile",
        "games-edit-profile",
        "games-duplicate-profile",
        "games-new-profile-wizard",
        "launch-dialog",
        "launch-dialog-unsaved",
        "launch-dialog-keep-as-new",
        "launch-post-play",
        "launch-post-play-older-point",
        "settings-clear-all",
        "first-run-templates",
        "editor-consent",
        "editor-reconnect",
    ];
    private const string _gitPath = @"C:\Program Files\SavePoint\git\cmd\git.exe";
    private const string _catalogDirectory = @"C:\Users\you\AppData\Roaming\SavePoint";

    private static FakeBackend StartupBackend(StartupStatus status) => new(ScenarioBuilder.Mockup().WithStartupStatus(status));

    private static ProfileRowViewModel Row(ComposedApp app, string id) => app.Games.Profiles.Single(row => row.Id == id);

    // Opens a state the way a person gets there.
    public static async Task<ComposedApp> OpenAsync(string state)
    {
        var app = state switch
        {
            "games-library" => await ComposedApp.StartWithViewAsync(layout: ShellLayout.Library),
            "history-filtered-empty" => await ComposedApp.StartWithViewAsync(new FakeBackend(ScenarioBuilder.Mockup()
                .WithSettings(MockupScenario.Settings)
                .WithProfile(new Profile("profile-empty", _skyrim, "Fresh profile", "profile/profile-empty")))),
            "first-run" or "first-run-templates" => await ComposedApp.StartWithViewAsync(new FakeBackend(ScenarioBuilder.Empty().WithSettings(MockupScenario.Settings))),
            "startup-git-unavailable" => await ComposedApp.StartWithViewAsync(StartupBackend(new StartupStatus(
                new GitStatus(false, _gitPath, null, ErrorInfo.From(new GitUnavailableException(_gitPath, "git exited with 127: not found", null))),
                new CatalogStatus(_catalogDirectory, []))), visitScreens: false),
            "startup-corrupt-catalog" => await ComposedApp.StartWithViewAsync(StartupBackend(new StartupStatus(
                new GitStatus(true, _gitPath, "2.56.0.windows.2", null),
                new CatalogStatus(_catalogDirectory, [
                    new CorruptCatalogFile(_catalogDirectory + @"\games.json", _catalogDirectory + @"\games.json.bak", ErrorInfo.From(new CatalogCorruptException(_catalogDirectory + @"\games.json", null))),
                    new CorruptCatalogFile(_catalogDirectory + @"\profiles.json", null, ErrorInfo.From(new CatalogCorruptException(_catalogDirectory + @"\profiles.json", null))),
                ]))), visitScreens: false),
            _ => await ComposedApp.StartWithViewAsync(),
        };

        switch (state)
        {
            case "games-banner":
                _ = Row(app, MockupScenario.ModdedId).PlayCommand.ExecuteAsync(null);
                break;
            case "games-banner-details":
                _ = Row(app, MockupScenario.ModdedId).PlayCommand.ExecuteAsync(null);
                _ = Assert.IsType<LaunchBannerViewModel>(app.Games.InlineContent).ShowDetailsCommand.ExecuteAsync(null);
                break;
            case "games-running":
                _ = Row(app, MockupScenario.ModdedId).PlayCommand.ExecuteAsync(null);
                app.Backend.Launch.ReportRunning(_skyrim, MockupScenario.RunningSince);
                break;
            case "games-checking":
                // The scan never returns, so the card that says it is checking stays on screen.
                app.Backend.Launch.PreviewGate = token => new TaskCompletionSource().Task.WaitAsync(token);
                _ = Row(app, MockupScenario.ModdedId).PlayCommand.ExecuteAsync(null);
                break;
            case "games-failure-card":
                app.Backend.FailNext<ILaunchApi>(nameof(ILaunchApi.GetPreviewAsync), MockupScenario.FileInUseError);
                _ = Row(app, MockupScenario.ModdedId).PlayCommand.ExecuteAsync(null);
                break;
            case "games-delete-game":
                _ = app.Games.DeleteGameCommand.ExecuteAsync(null);
                break;
            case "games-delete-profile":
                _ = Row(app, MockupScenario.VanillaId).DeleteCommand.ExecuteAsync(null);
                break;
            case "games-edit-profile":
                _ = Row(app, MockupScenario.VanillaId).EditCommand.ExecuteAsync(null);
                break;
            case "games-duplicate-profile":
                _ = Row(app, MockupScenario.VanillaId).DuplicateCommand.ExecuteAsync(null);
                break;
            case "games-new-profile-wizard":
                _ = app.Games.NewProfileCommand.ExecuteAsync(null);
                break;
            case "launch-dialog":
                _ = Row(app, MockupScenario.VanillaId).PlayCommand.ExecuteAsync(null);
                break;
            case "launch-dialog-unsaved":
                _ = Row(app, MockupScenario.SurvivalId).PlayCommand.ExecuteAsync(null);
                break;
            case "launch-dialog-keep-as-new":
                _ = Row(app, MockupScenario.SurvivalId).PlayCommand.ExecuteAsync(null);
                _ = app.Dialog<LaunchDialogViewModel>().KeepCommand.ExecuteAsync(null);
                break;
            case "launch-progress":
                var play = Row(app, MockupScenario.VanillaId).PlayCommand.ExecuteAsync(null);
                app.Dialog<LaunchDialogViewModel>().SwitchCommand.Execute(null);
                await play.WaitAsync(TimeSpan.FromSeconds(5));
                app.Backend.Launch.ReportSwitching(_skyrim, MockupScenario.SwitchProgress);
                break;
            case "launch-post-play":
                _ = Row(app, MockupScenario.ModdedId).PlayCommand.ExecuteAsync(null);
                app.Backend.Launch.ReportRunning(_skyrim, MockupScenario.RunningSince);
                app.Backend.Launch.ReportExited(_skyrim, MockupScenario.PlayDuration, MockupScenario.NewSaveFiles);
                break;
            case "launch-post-play-older-point":
                app.History.Groups.SelectMany(group => group.Cards).Single(card => card.Name == "Cleared Blackreach").GoBackCommand.Execute(null);
                break;
            case "history-cards":
                app.Show<HistoryScreenViewModel>(NavDestination.History);
                break;
            case "history-list":
                app.Show<HistoryScreenViewModel>(NavDestination.History).SelectedViewOption = app.History.ViewOptions[1];
                break;
            case "history-filtered-empty":
                app.Show<HistoryScreenViewModel>(NavDestination.History).SelectedFilter =
                    app.History.Filters.Single(filter => filter.ProfileId == "profile-empty");
                break;
            case "history-empty":
                app.Shell.SelectGame(MockupScenario.SatisfactoryId);
                app.Show<HistoryScreenViewModel>(NavDestination.History);
                break;
            case "settings":
                app.Show<SettingsScreenViewModel>(NavDestination.Settings);
                break;
            case "settings-clear-all":
                app.Show<SettingsScreenViewModel>(NavDestination.Settings);
                _ = app.Settings.ClearAll.RemoveCommand.ExecuteAsync(null);
                break;
            case "first-run-templates":
                app.Shell.FirstRun.StartFromTemplateCommand.Execute(null);
                break;
            case "toast":
                app.Toasts.Show(new ToastMessage("Something went wrong.", ToastKind.Error, "The details."));
                break;
            case "job-pill":
                FakeAssert.Ok(await app.Backend.Games.OptimizeStorageAsync(_skyrim));
                break;
            case "editor-existing":
                app.Games.EditGameCommand.Execute(null);
                break;
            case "editor-new":
                app.Shell.RequestAddGame();
                break;
            case "editor-consent":
                _ = app.Main.Dialogs.ShowAsync(new ConsentDialogViewModel([new ConsentFolder("Game folder", MockupScenario.SkyrimInstall), new ConsentFolder("Saves folder", MockupScenario.SkyrimSaves)], app.Text));
                break;
            case "editor-reconnect":
                _ = app.Main.Dialogs.ShowAsync(new ReconnectDialogViewModel(
                    new OrphanedHistory("old-game", [RepoRole.Install, RepoRole.External], [new RecoverableProfile("profile/one", "Modded run"), new RecoverableProfile("profile/two", null)]),
                    app.Text));
                break;
        }

        app.Settle();
        return app;
    }
}
