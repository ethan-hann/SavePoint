using CommunityToolkit.Mvvm.Input;
using Savepoint.App.Features.Activity;
using Savepoint.App.Features.Common.Toasts;
using Savepoint.App.Features.Editor;
using Savepoint.App.Features.Games;
using Savepoint.App.Features.History;
using Savepoint.App.Features.Launch;
using Savepoint.App.Features.Library;
using Savepoint.App.Features.Settings;
using Savepoint.App.Features.Shell;
using Savepoint.App.Tests.Fakes;
using Savepoint.App.Tests.Features.Activity;
using Savepoint.App.Tests.Features.Library;
using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;

namespace Savepoint.App.Tests.Features.EndToEnd;

// Every screen and dialog of the app in the states a person meets them, reached by using the real app.
internal static class AppStates
{
    private const string _skyrim = SampleScenario.SkyrimId;

    public static TheoryData<string> All =>
    [
        "games-sidebar",
        "games-library",
        "games-banner",
        "games-banner-details",
        "games-running",
        "games-checking",
        "games-loading",
        "games-failure-card",
        "games-delete-game",
        "games-delete-profile",
        "games-edit-profile",
        "games-duplicate-profile",
        "games-new-profile-wizard",
        "launch-dialog",
        "launch-dialog-unsaved",
        "launch-dialog-saving",
        "launch-dialog-keep-as-new",
        "launch-discard-first-time",
        "launch-progress",
        "launch-post-play",
        "launch-post-play-older-point",
        "launch-steam-card",
        "launch-steam-waiting",
        "launch-checking-closed",
        "post-play-from-older-snapshot",
        "post-play-from-older-snapshot-again",
        "history-cards",
        "history-list",
        "history-filtered-empty",
        "history-empty",
        "history-help-open",
        "settings-help-open",
        "launch-dialog-help-open",
        "post-play-replace-help-open",
        "activity-jobs",
        "activity-failed",
        "activity-empty",
        "settings",
        "settings-clear-all",
        "how-it-works",
        "tour-step-1",
        "tour-step-2",
        "tour-step-3",
        "tour-step-4",
        "tour-library-step-3",
        "tour-library-step-4",
        "first-run",
        "first-run-templates",
        "editor-existing",
        "editor-new",
        "editor-consent",
        "editor-reconnect",
        "library-picker",
        "library-picker-many",
        "library-confirm",
        "library-add-done",
        "startup-git-unavailable",
        "startup-corrupt-catalog",
        "startup-storage-unreachable",
        "library-error",
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
        "launch-discard-first-time",
        "launch-post-play",
        "launch-post-play-older-point",
        "post-play-from-older-snapshot-again",
        "settings-clear-all",
        "first-run-templates",
        "editor-consent",
        "editor-reconnect",
        "library-picker",
        "library-picker-many",
        "library-confirm",
        "library-add-done",
    ];
    private const string _gitPath = @"C:\Program Files\SavePoint\git\cmd\git.exe";
    private const string _catalogDirectory = @"C:\Users\you\AppData\Roaming\SavePoint";
    private const string _storageRoot = @"F:\SavePoint";

    // The first read of the game list fails, as a catalog that cannot be reached would.
    private static FakeBackend LibraryUnreadable()
    {
        var backend = FakeBackend.Sample();
        backend.FailNext<IGamesApi>(nameof(IGamesApi.ListAsync), ErrorInfo.From(new CatalogCorruptException(_catalogDirectory + @"\games.json", null)));
        return backend;
    }

    // Jobs that ran before the app started are history: no toast for each, so the Activity screen is audited with the room it really has.
    private static async Task<FakeBackend> BackendWithJobsAsync()
    {
        var backend = FakeBackend.Sample();
        await ActivityScenario.AddJobsAsync(backend);
        return backend;
    }

    private static FakeBackend UnseenTour(ShellLayout layout) =>
        new(ScenarioBuilder.Sample().WithSettings(SampleScenario.Settings with { TourSeen = false, Layout = layout }));

    private static FakeBackend StartupBackend(StartupStatus status) => new(ScenarioBuilder.Sample().WithStartupStatus(status));

    private static ProfileRowViewModel Row(ComposedApp app, string id) => app.Games.Profiles.Single(row => row.Id == id);

    // Plays from the older snapshot "Cleared Blackreach" to the end of the session, so the dialog after playing offers a new profile or
    // continuing the old one.
    public static async Task PlayFromOlderSnapshotAsync(this ComposedApp app)
    {
        var goBack = Assert.IsAssignableFrom<IAsyncRelayCommand>(app.History.Groups.SelectMany(group => group.Cards).Single(card => card.Name == "Cleared Blackreach").GoBackCommand).ExecuteAsync(null);
        app.Dialog<LaunchDialogViewModel>().SwitchCommand.Execute(null);
        await goBack.WaitAsync(TimeSpan.FromSeconds(5));
        app.Backend.Launch.ReportRunning(_skyrim, SampleScenario.RunningSince);
        app.Backend.Launch.ReportExited(_skyrim, SampleScenario.PlayDuration, SampleScenario.NewSaveFiles);
    }

    // Opens a state the way a person gets there.
    public static async Task<ComposedApp> OpenAsync(string state)
    {
        var app = state switch
        {
            "games-library" => await ComposedApp.StartWithViewAsync(layout: ShellLayout.Library),
            "history-filtered-empty" => await ComposedApp.StartWithViewAsync(new FakeBackend(ScenarioBuilder.Sample()
                .WithSettings(SampleScenario.Settings)
                .WithProfile(new Profile("profile-empty", _skyrim, "Fresh profile", "profile/profile-empty")))),
            "activity-jobs" or "activity-failed" => await ComposedApp.StartWithViewAsync(await BackendWithJobsAsync()),
            // The tour starts by itself for a player who has not seen it; the steps are reached with its own Next button.
            "tour-step-1" or "tour-step-2" or "tour-step-3" or "tour-step-4" => await ComposedApp.StartWithViewAsync(UnseenTour(ShellLayout.Sidebar)),
            "tour-library-step-3" or "tour-library-step-4" => await ComposedApp.StartWithViewAsync(UnseenTour(ShellLayout.Library)),
            // A player who has met the older-snapshot choice before: its explanation starts collapsed, so Escape goes to the dialog.
            "post-play-from-older-snapshot-again" => await ComposedApp.StartWithViewAsync(new FakeBackend(ScenarioBuilder.Sample().WithSettings(SampleScenario.Settings with { OlderChoiceHelpSeen = true }))),
            "first-run" or "first-run-templates" => await ComposedApp.StartWithViewAsync(new FakeBackend(ScenarioBuilder.Empty().WithSettings(SampleScenario.Settings))),
            "library-error" => await ComposedApp.StartWithViewAsync(LibraryUnreadable(), visitScreens: false),
            "startup-git-unavailable" => await ComposedApp.StartWithViewAsync(StartupBackend(new StartupStatus(
                new GitStatus(false, _gitPath, null, ErrorInfo.From(new GitUnavailableException(_gitPath, "git exited with 127: not found", null))),
                new CatalogStatus(_catalogDirectory, []))), visitScreens: false),
            "startup-corrupt-catalog" => await ComposedApp.StartWithViewAsync(StartupBackend(new StartupStatus(
                new GitStatus(true, _gitPath, "2.56.0.windows.2", null),
                new CatalogStatus(_catalogDirectory, [
                    new CorruptCatalogFile(_catalogDirectory + @"\games.json", _catalogDirectory + @"\games.json.bak", ErrorInfo.From(new CatalogCorruptException(_catalogDirectory + @"\games.json", null))),
                    new CorruptCatalogFile(_catalogDirectory + @"\profiles.json", null, ErrorInfo.From(new CatalogCorruptException(_catalogDirectory + @"\profiles.json", null))),
                ]))), visitScreens: false),
            "startup-storage-unreachable" => await ComposedApp.StartWithViewAsync(StartupBackend(new StartupStatus(
                new GitStatus(true, _gitPath, "2.56.0.windows.2", null),
                new CatalogStatus(_catalogDirectory, []),
                new StorageStatus(false, _storageRoot, ErrorInfo.From(new StorageUnreachableException(_storageRoot, null))))), visitScreens: false),
            _ => await ComposedApp.StartWithViewAsync(),
        };

        switch (state)
        {
            case "games-banner":
                _ = Row(app, SampleScenario.ModdedId).PlayCommand.ExecuteAsync(null);
                break;
            case "games-banner-details":
                _ = Row(app, SampleScenario.ModdedId).PlayCommand.ExecuteAsync(null);
                _ = Assert.IsType<LaunchBannerViewModel>(app.Games.InlineContent).ShowDetailsCommand.ExecuteAsync(null);
                break;
            case "games-running":
                _ = Row(app, SampleScenario.ModdedId).PlayCommand.ExecuteAsync(null);
                app.Backend.Launch.ReportRunning(_skyrim, SampleScenario.RunningSince);
                break;
            case "games-checking":
                // The scan never returns, so the card that says it is checking stays on screen.
                app.Backend.Launch.PreviewGate = token => new TaskCompletionSource().Task.WaitAsync(token);
                _ = Row(app, SampleScenario.ModdedId).PlayCommand.ExecuteAsync(null);
                break;
            case "games-loading":
                // The first read of another game never returns, so the loading panel stays on screen.
                app.Backend.Profiles.ListGate = token => new TaskCompletionSource().Task.WaitAsync(token);
                app.Shell.SelectGame(SampleScenario.SatisfactoryId);
                break;
            case "games-failure-card":
                app.Backend.FailNext<ILaunchApi>(nameof(ILaunchApi.GetPreviewAsync), SampleScenario.FileInUseError);
                _ = Row(app, SampleScenario.ModdedId).PlayCommand.ExecuteAsync(null);
                break;
            case "games-delete-game":
                _ = app.Games.DeleteGameCommand.ExecuteAsync(null);
                break;
            case "games-delete-profile":
                _ = Row(app, SampleScenario.VanillaId).DeleteCommand.ExecuteAsync(null);
                break;
            case "games-edit-profile":
                _ = Row(app, SampleScenario.VanillaId).EditCommand.ExecuteAsync(null);
                break;
            case "games-duplicate-profile":
                _ = Row(app, SampleScenario.VanillaId).DuplicateCommand.ExecuteAsync(null);
                break;
            case "games-new-profile-wizard":
                _ = app.Games.NewProfileCommand.ExecuteAsync(null);
                break;
            case "launch-dialog":
                _ = Row(app, SampleScenario.VanillaId).PlayCommand.ExecuteAsync(null);
                break;
            case "launch-dialog-unsaved":
                _ = Row(app, SampleScenario.SurvivalId).PlayCommand.ExecuteAsync(null);
                break;
            case "launch-discard-first-time":
                // The first discard confirmation a player is asked: it carries one more line of advice.
                _ = Row(app, SampleScenario.SurvivalId).PlayCommand.ExecuteAsync(null);
                _ = app.Dialog<LaunchDialogViewModel>().UnsavedBlocks[0].DiscardCommand.ExecuteAsync(null);
                break;
            case "launch-dialog-saving":
                _ = Row(app, SampleScenario.SurvivalId).PlayCommand.ExecuteAsync(null);
                _ = app.Dialog<LaunchDialogViewModel>().UnsavedBlocks[0].SaveFirstCommand.ExecuteAsync(null);
                app.Backend.Jobs.ReportProgress(app.Backend.Jobs.LastStarted(JobKind.Capture).JobId, new JobProgress(FilesDone: 410, FilesTotal: 1000));
                break;
            case "launch-dialog-keep-as-new":
                _ = Row(app, SampleScenario.SurvivalId).PlayCommand.ExecuteAsync(null);
                _ = app.Dialog<LaunchDialogViewModel>().KeepCommand.ExecuteAsync(null);
                break;
            case "launch-progress":
                var play = Row(app, SampleScenario.VanillaId).PlayCommand.ExecuteAsync(null);
                app.Dialog<LaunchDialogViewModel>().SwitchCommand.Execute(null);
                await play.WaitAsync(TimeSpan.FromSeconds(5));
                app.Backend.Launch.ReportSwitching(_skyrim, SampleScenario.SwitchProgress);
                break;
            case "launch-post-play":
                _ = Row(app, SampleScenario.ModdedId).PlayCommand.ExecuteAsync(null);
                app.Backend.Launch.ReportRunning(_skyrim, SampleScenario.RunningSince);
                app.Backend.Launch.ReportExited(_skyrim, SampleScenario.PlayDuration, SampleScenario.NewSaveFiles);
                break;
            case "launch-steam-card":
                // Steam is closed for a Steam game: the card asks before anything is read or switched.
                var steamTarget = new LaunchTarget.ProfileTip(SampleScenario.ModdedId);
                var steamPreview = FakeAssert.Ok(await app.Backend.Launch.GetPreviewAsync(_skyrim, steamTarget));
                app.Backend.State.LaunchPreviews[(_skyrim, steamTarget)] = steamPreview with { SteamNeeded = ErrorInfo.From(new SteamNotRunningException(_skyrim)) };
                _ = Row(app, SampleScenario.ModdedId).PlayCommand.ExecuteAsync(null);
                break;
            case "launch-steam-waiting":
                await Row(app, SampleScenario.ModdedId).PlayCommand.ExecuteAsync(null).WaitAsync(TimeSpan.FromSeconds(5));
                app.Backend.Jobs.ReportStep(FakeAssert.Session(app.Backend, _skyrim).LaunchJobId, JobSteps.LaunchWaitingForSteam, ("game", "Skyrim Special Edition"));
                break;
            case "launch-checking-closed":
                // The check for a game started again never answers, so the card that says it is checking stays on screen.
                _ = Row(app, SampleScenario.ModdedId).PlayCommand.ExecuteAsync(null);
                app.Backend.Launch.ReportRunning(_skyrim, SampleScenario.RunningSince);
                app.Backend.Launch.ReportExited(_skyrim, SampleScenario.PlayDuration, SampleScenario.NewSaveFiles);
                app.Backend.Launch.CompleteGate = () => new TaskCompletionSource().Task;
                _ = app.Dialog<PostPlayDialogViewModel>().SaveCommand.ExecuteAsync(null);
                break;
            case "launch-post-play-older-point":
                app.History.Groups.SelectMany(group => group.Cards).Single(card => card.Name == "Cleared Blackreach").GoBackCommand.Execute(null);
                break;
            case "post-play-from-older-snapshot" or "post-play-from-older-snapshot-again":
                await app.PlayFromOlderSnapshotAsync();
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
                app.Shell.SelectGame(SampleScenario.SatisfactoryId);
                app.Show<HistoryScreenViewModel>(NavDestination.History);
                break;
            case "activity-jobs":
                app.Show<ActivityScreenViewModel>(NavDestination.Activity);
                break;
            case "activity-failed":
                var activity = app.Show<ActivityScreenViewModel>(NavDestination.Activity);
                activity.SelectedRow = activity.Rows.Single(row => row.StatusText == "Failed");
                break;
            case "activity-empty":
                app.Show<ActivityScreenViewModel>(NavDestination.Activity);
                break;
            case "settings":
                app.Show<SettingsScreenViewModel>(NavDestination.Settings);
                break;
            case "settings-clear-all":
                app.Show<SettingsScreenViewModel>(NavDestination.Settings);
                _ = app.Settings.ClearAll.RemoveCommand.ExecuteAsync(null);
                break;
            case "how-it-works":
                app.Shell.ShowHelp();
                break;
            case "tour-step-2" or "tour-step-3" or "tour-step-4" or "tour-library-step-3" or "tour-library-step-4":
                for (var step = 1; step < int.Parse(state[^1..], System.Globalization.CultureInfo.InvariantCulture); step++)
                {
                    app.Shell.Tour.NextCommand.Execute(null);
                }

                break;
            case "history-help-open":
                app.Show<HistoryScreenViewModel>(NavDestination.History).SnapshotHelp.IsOpen = true;
                break;
            case "settings-help-open":
                app.Show<SettingsScreenViewModel>(NavDestination.Settings).SpaceHelp.IsOpen = true;
                break;
            case "launch-dialog-help-open":
                _ = Row(app, SampleScenario.SurvivalId).PlayCommand.ExecuteAsync(null);
                var checks = app.Dialog<LaunchDialogViewModel>();
                checks.SaveSnapshotHelp.IsOpen = true;
                checks.UnsavedBlocks[0].Help!.IsOpen = true;
                break;
            case "post-play-replace-help-open":
                _ = Row(app, SampleScenario.ModdedId).PlayCommand.ExecuteAsync(null);
                app.Backend.Launch.ReportRunning(_skyrim, SampleScenario.RunningSince);
                app.Backend.Launch.ReportExited(_skyrim, SampleScenario.PlayDuration, SampleScenario.NewSaveFiles);
                var afterPlay = app.Dialog<PostPlayDialogViewModel>();
                afterPlay.SelectedChoice = afterPlay.Choices.Single(choice => choice.Kind == CompletionChoice.UpdateExisting);
                afterPlay.SelectedChoice.Help!.IsOpen = true;
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
                _ = app.Main.Dialogs.ShowAsync(new ConsentDialogViewModel([new ConsentFolder("Game folder", SampleScenario.SkyrimInstall), new ConsentFolder("Saves folder", SampleScenario.SkyrimSaves)], app.Text));
                break;
            case "editor-reconnect":
                _ = app.Main.Dialogs.ShowAsync(new ReconnectDialogViewModel(
                    new OrphanedHistory("old-game", [RepoRole.Install, RepoRole.External], [new RecoverableProfile("profile/one", "Modded run"), new RecoverableProfile("profile/two", null)]),
                    app.Text));
                break;
            case "library-picker" or "library-picker-many":
                app.Backend.State.Library = LibraryStates.Scan(many: state.EndsWith("many", StringComparison.Ordinal));
                var checklist = new LibraryPickerDialogViewModel(app.Backend.Games, app.Text);
                _ = app.Main.Dialogs.ShowAsync(checklist);
                await checklist.LoadAsync();
                break;
            case "library-confirm":
                _ = app.Main.Dialogs.ShowAsync(new LibraryConfirmDialogViewModel(LibraryStates.Scan(many: false).Games.Take(3).ToList(), app.Text));
                break;
            case "library-add-done":
                app.Backend.State.Library = LibraryStates.Scan(many: false);
                var adding = new LibraryAddDialogViewModel(app.Backend.Games, app.Backend.State.Library.Games, app.Text);
                _ = app.Main.Dialogs.ShowAsync(adding);
                await adding.RunAsync();
                break;
        }

        app.Settle();
        return app;
    }
}
