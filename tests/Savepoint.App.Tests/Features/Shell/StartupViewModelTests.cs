using Savepoint.App.Features.Common;
using Savepoint.App.Features.Common.Formatting;
using Savepoint.App.Features.Shell;
using Savepoint.App.Hosting;
using Savepoint.App.Localization;
using Savepoint.App.Tests.Fakes;
using Savepoint.Core.Api;
using Savepoint.Core.Errors;

namespace Savepoint.App.Tests.Features.Shell;

public class StartupViewModelTests
{
    private const string _gitPath = @"C:\Program Files\SavePoint\git\cmd\git.exe";
    private const string _catalogDirectory = @"C:\Users\you\AppData\Roaming\SavePoint";

    private static readonly UiFormatter _text = new(LocalizationCatalog.LoadEmbedded("en", throwOnMissingKey: true), os: "windows", timeZone: TimeZoneInfo.Utc);

    private static StartupStatus Healthy() => new(new GitStatus(true, _gitPath, "2.56.0.windows.2", null), new CatalogStatus(_catalogDirectory, []));

    private static StartupStatus NoGit(bool withError = true) => new(
        new GitStatus(false, _gitPath, null, withError ? ErrorInfo.From(new GitUnavailableException(_gitPath, "git exited with 127", null)) : null),
        new CatalogStatus(_catalogDirectory, []));

    private static CorruptCatalogFile Corrupt(string name, string? backup) =>
        new(Path.Combine(_catalogDirectory, name).Replace('/', '\\'), backup, ErrorInfo.From(new CatalogCorruptException(Path.Combine(_catalogDirectory, name).Replace('/', '\\'), null)));

    private static StartupStatus WithCorrupt(params CorruptCatalogFile[] files) =>
        new(new GitStatus(true, _gitPath, "2.56.0.windows.2", null), new CatalogStatus(_catalogDirectory, files));

    private static FakeBackend Backend(StartupStatus status, params string[] foldersWithHistory) =>
        new(ScenarioBuilder.Sample().WithStartupStatus(status).WithStorageHistoryIn(foldersWithHistory));

    [Fact]
    public async Task A_healthy_start_is_ready_and_says_so_once()
    {
        var backend = Backend(Healthy());
        var startup = new StartupViewModel(backend.AppStatus, _text);
        var ready = 0;
        startup.Ready += (_, _) => ready++;

        await startup.CheckAsync();

        Assert.Equal(StartupPhase.Ready, startup.Phase);
        Assert.Equal(1, ready);
        Assert.Null(startup.Card);
        Assert.Empty(startup.CorruptFiles);
    }

    [Fact]
    public void A_new_view_model_is_still_checking()
    {
        var startup = new StartupViewModel(Backend(Healthy()).AppStatus, _text);

        Assert.Equal(StartupPhase.Checking, startup.Phase);
    }

    [Fact]
    public async Task While_an_earlier_cleanup_is_finished_the_screen_says_so_and_stays_in_the_checking_phase()
    {
        var backend = Backend(Healthy());
        var release = new TaskCompletionSource();
        backend.State.StartupSteps.Add(StartupProgress.FinishingEarlierCleanup);
        backend.State.StartupGate = release.Task;
        var startup = new StartupViewModel(backend.AppStatus, _text);
        var ready = 0;
        startup.Ready += (_, _) => ready++;

        var check = startup.CheckAsync();
        var duringWait = (startup.Phase, startup.IsFinishingEarlierCleanup, ready);
        release.SetResult();
        await check;

        Assert.Equal((StartupPhase.Checking, true, 0), duringWait);
        Assert.Equal(StartupPhase.Ready, startup.Phase);
        Assert.False(startup.IsFinishingEarlierCleanup);
        Assert.Equal(1, ready);
    }

    [Fact]
    public async Task While_a_task_started_before_SavePoint_closed_is_waited_for_the_screen_says_so_and_not_the_cleanup_note()
    {
        var backend = Backend(Healthy());
        var release = new TaskCompletionSource();
        backend.State.StartupSteps.Add(StartupProgress.WaitingForEarlierTask);
        backend.State.StartupGate = release.Task;
        var startup = new StartupViewModel(backend.AppStatus, _text);

        var check = startup.CheckAsync();
        var duringWait = (startup.Phase, startup.IsWaitingForEarlierTask, startup.IsFinishingEarlierCleanup);
        release.SetResult();
        await check;

        Assert.Equal((StartupPhase.Checking, true, false), duringWait);
        Assert.Equal(StartupPhase.Ready, startup.Phase);
        Assert.False(startup.IsWaitingForEarlierTask);
    }

    [Fact]
    public async Task A_check_with_nothing_left_behind_never_shows_the_cleanup_note()
    {
        var startup = new StartupViewModel(Backend(Healthy()).AppStatus, _text);
        var shown = false;
        startup.PropertyChanged += (_, e) => shown |= e.PropertyName == nameof(StartupViewModel.IsFinishingEarlierCleanup) && startup.IsFinishingEarlierCleanup;

        await startup.CheckAsync();

        Assert.False(shown);
        Assert.False(startup.IsFinishingEarlierCleanup);
    }

    [Fact]
    public async Task Without_the_bundled_tool_the_screen_names_the_path_and_says_to_reinstall()
    {
        var startup = new StartupViewModel(Backend(NoGit()).AppStatus, _text);
        var ready = 0;
        startup.Ready += (_, _) => ready++;

        await startup.CheckAsync();

        Assert.Equal(StartupPhase.GitUnavailable, startup.Phase);
        Assert.Equal(0, ready);
        var card = startup.Card!;
        Assert.Equal("SavePoint's built-in tool is missing", card.Title);
        Assert.Contains("Reinstall SavePoint", card.Body);
        Assert.Equal(_gitPath, card.Path);
        Assert.Contains("git exited with 127", card.SupportText);
        Assert.Equal(["Try again"], card.Actions.Select(action => action.Label));
        Assert.True(card.Actions[0].IsPrimary);
    }

    [Fact]
    public async Task A_missing_tool_without_an_error_still_shows_the_path_it_tried()
    {
        var startup = new StartupViewModel(Backend(NoGit(withError: false)).AppStatus, _text);

        await startup.CheckAsync();

        Assert.Equal(StartupPhase.GitUnavailable, startup.Phase);
        Assert.Equal(_gitPath, startup.Card!.Path);
        Assert.Contains("Reinstall SavePoint", startup.Card.Body);
    }

    [Fact]
    public async Task Trying_again_after_a_fix_gets_past_the_screen()
    {
        var backend = Backend(NoGit());
        var startup = new StartupViewModel(backend.AppStatus, _text);
        var ready = 0;
        startup.Ready += (_, _) => ready++;
        await startup.CheckAsync();

        backend.State.Startup = Healthy();
        await startup.Card!.Actions[0].Command.ExecuteAsync();

        Assert.Equal(StartupPhase.Ready, startup.Phase);
        Assert.Equal(1, ready);
        Assert.Equal(2, backend.Calls.To<IAppStatusApi>(nameof(IAppStatusApi.CheckAsync)).Count);
    }

    [Fact]
    public async Task Unreadable_catalog_files_are_listed_each_with_its_path_and_its_choices()
    {
        var withBackup = Corrupt("games.json", @"C:\Users\you\AppData\Roaming\SavePoint\games.json.bak");
        var without = Corrupt("profiles.json", null);
        var startup = new StartupViewModel(Backend(WithCorrupt(withBackup, without)).AppStatus, _text);

        await startup.CheckAsync();

        Assert.Equal(StartupPhase.CatalogCorrupt, startup.Phase);
        Assert.Equal([withBackup.Path, without.Path], startup.CorruptFiles.Select(file => file.Card.Path));
        var first = startup.CorruptFiles[0].Card;
        Assert.Equal("Your game list couldn't be read", first.Title);
        Assert.Equal(["Restore the backup", "Start with nothing in it"], first.Actions.Select(action => action.Label));
        Assert.Equal(@"Last good copy: C:\Users\you\AppData\Roaming\SavePoint\games.json.bak", first.Supplement);
        Assert.Equal("There is no backup copy of this file.", startup.CorruptFiles[1].Card.Supplement);
    }

    [Theory]
    [InlineData("games.json", "Your game list couldn't be read", "your list of games", "no games listed")]
    [InlineData("profiles.json", "Your profile list couldn't be read", "your list of profiles", "no profiles listed")]
    [InlineData("settings.json", "Your settings couldn't be read", "your settings", "the default settings")]
    [InlineData("other.json", "SavePoint couldn't read one of its files", "some of its data", "an empty file")]
    public async Task Each_kind_of_file_gets_its_own_title_and_a_body_that_says_what_the_file_holds_and_that_it_was_left_alone(string name, string title, string holds, string empty)
    {
        var startup = new StartupViewModel(Backend(WithCorrupt(Corrupt(name, @"C:\x\backup.bak"))).AppStatus, _text);

        await startup.CheckAsync();

        var card = startup.CorruptFiles.Single().Card;
        Assert.Equal(title, card.Title);
        Assert.Equal(
            $"SavePoint couldn't read the file that holds {holds}, so it left the file as it is.",
            card.Body);
        Assert.DoesNotContain(empty, card.Body);
        Assert.DoesNotContain(".bak", card.Body);
    }

    [Fact]
    public async Task Starting_with_nothing_asks_first_and_says_what_will_be_empty_and_that_the_old_file_is_kept()
    {
        var file = Corrupt("games.json", null);
        var backend = Backend(WithCorrupt(file));
        var dialogs = new FakeDialogs().AnswerConfirm(false);
        var startup = new StartupViewModel(backend.AppStatus, _text, dialogs: dialogs);
        await startup.CheckAsync();

        await startup.CorruptFiles[0].Card.Actions[1].Command.ExecuteAsync();

        var asked = Assert.Single(dialogs.Confirmations);
        Assert.Equal("Start with nothing in it?", asked.Title);
        Assert.Equal("SavePoint will move the unreadable file aside, never delete it, and start with no games listed.", asked.Message);
        Assert.Equal("Start with nothing in it", asked.ConfirmLabel);
        Assert.True(asked.Destructive);
        Assert.Empty(backend.Calls.To<IAppStatusApi>(nameof(IAppStatusApi.RecoverCatalogFileAsync)));
        Assert.Equal(StartupPhase.CatalogCorrupt, startup.Phase);
    }

    [Fact]
    public async Task Confirming_starts_with_nothing_and_restoring_a_backup_never_asks()
    {
        var withBackup = Corrupt("games.json", @"C:\x\games.json.bak");
        var without = Corrupt("profiles.json", null);
        var backend = Backend(WithCorrupt(withBackup, without));
        var dialogs = new FakeDialogs();
        var startup = new StartupViewModel(backend.AppStatus, _text, dialogs: dialogs);
        await startup.CheckAsync();

        await startup.CorruptFiles[0].Card.Actions[0].Command.ExecuteAsync();
        Assert.Empty(dialogs.Confirmations);

        await startup.CorruptFiles[0].Card.Actions[1].Command.ExecuteAsync();

        Assert.Single(dialogs.Confirmations);
        var recoveries = backend.Calls.To<IAppStatusApi>(nameof(IAppStatusApi.RecoverCatalogFileAsync));
        Assert.Equal([withBackup.Path, CatalogRecovery.RestoreBackup], recoveries[0].Args);
        Assert.Equal([without.Path, CatalogRecovery.StartEmpty], recoveries[1].Args);
    }

    [Fact]
    public async Task Restoring_the_backup_is_only_offered_when_there_is_one()
    {
        var withBackup = Corrupt("games.json", @"C:\x\games.json.bak");
        var without = Corrupt("profiles.json", null);
        var startup = new StartupViewModel(Backend(WithCorrupt(withBackup, without)).AppStatus, _text);
        await startup.CheckAsync();

        Assert.True(startup.CorruptFiles[0].Card.Actions[0].Command.CanExecute(null));
        Assert.False(startup.CorruptFiles[1].Card.Actions[0].Command.CanExecute(null));
        Assert.True(startup.CorruptFiles[1].Card.Actions[1].Command.CanExecute(null));
    }

    [Fact]
    public async Task Restoring_a_backup_recovers_the_file_notes_where_it_went_and_checks_again()
    {
        var file = Corrupt("games.json", @"C:\x\games.json.bak");
        var backend = Backend(WithCorrupt(file));
        var startup = new StartupViewModel(backend.AppStatus, _text);
        var ready = 0;
        startup.Ready += (_, _) => ready++;
        await startup.CheckAsync();

        await startup.CorruptFiles[0].Card.Actions[0].Command.ExecuteAsync();

        var recover = Assert.Single(backend.Calls.To<IAppStatusApi>(nameof(IAppStatusApi.RecoverCatalogFileAsync)));
        Assert.Equal([file.Path, CatalogRecovery.RestoreBackup], recover.Args);
        Assert.Equal(StartupPhase.Ready, startup.Phase);
        Assert.Equal(1, ready);
        Assert.Equal([$"The unreadable file was moved to {file.Path}.corrupt."], startup.RecoveryNotes);
    }

    [Fact]
    public async Task Starting_empty_recovers_with_that_choice()
    {
        var file = Corrupt("games.json", null);
        var backend = Backend(WithCorrupt(file));
        var startup = new StartupViewModel(backend.AppStatus, _text);
        await startup.CheckAsync();

        await startup.CorruptFiles[0].Card.Actions[1].Command.ExecuteAsync();

        var recover = Assert.Single(backend.Calls.To<IAppStatusApi>(nameof(IAppStatusApi.RecoverCatalogFileAsync)));
        Assert.Equal([file.Path, CatalogRecovery.StartEmpty], recover.Args);
        Assert.Equal(StartupPhase.Ready, startup.Phase);
    }

    [Fact]
    public async Task Recovering_one_of_two_files_leaves_the_other_on_screen()
    {
        var first = Corrupt("games.json", @"C:\x\games.json.bak");
        var second = Corrupt("profiles.json", @"C:\x\profiles.json.bak");
        var startup = new StartupViewModel(Backend(WithCorrupt(first, second)).AppStatus, _text);
        await startup.CheckAsync();

        await startup.CorruptFiles[0].Card.Actions[0].Command.ExecuteAsync();

        Assert.Equal(StartupPhase.CatalogCorrupt, startup.Phase);
        Assert.Equal([second.Path], startup.CorruptFiles.Select(file => file.File.Path));
        Assert.Single(startup.RecoveryNotes);
    }

    [Fact]
    public async Task A_recovery_that_fails_says_why_and_stays_on_the_screen()
    {
        var file = Corrupt("games.json", @"C:\x\games.json.bak");
        var backend = Backend(WithCorrupt(file));
        var startup = new StartupViewModel(backend.AppStatus, _text);
        await startup.CheckAsync();
        backend.FailNext<IAppStatusApi>(nameof(IAppStatusApi.RecoverCatalogFileAsync), ErrorInfo.From(new InvalidOperationException("locked")));

        await startup.CorruptFiles[0].Card.Actions[0].Command.ExecuteAsync();

        Assert.Equal(StartupPhase.CatalogCorrupt, startup.Phase);
        Assert.Equal("Something went wrong. Try again.", startup.RecoveryError);
        Assert.Empty(startup.RecoveryNotes);
        Assert.Single(backend.Calls.To<IAppStatusApi>(nameof(IAppStatusApi.CheckAsync)));
    }

    [Fact]
    public async Task A_later_recovery_clears_the_earlier_error()
    {
        var file = Corrupt("games.json", @"C:\x\games.json.bak");
        var backend = Backend(WithCorrupt(file));
        var startup = new StartupViewModel(backend.AppStatus, _text);
        await startup.CheckAsync();
        backend.FailNext<IAppStatusApi>(nameof(IAppStatusApi.RecoverCatalogFileAsync), ErrorInfo.From(new InvalidOperationException("locked")));
        await startup.CorruptFiles[0].Card.Actions[0].Command.ExecuteAsync();

        await startup.CorruptFiles[0].Card.Actions[1].Command.ExecuteAsync();

        Assert.Null(startup.RecoveryError);
    }

    [Fact]
    public async Task The_tool_is_reported_before_the_catalog_when_both_are_wrong()
    {
        var status = new StartupStatus(NoGit().Git, new CatalogStatus(_catalogDirectory, [Corrupt("games.json", null)]));
        var startup = new StartupViewModel(Backend(status).AppStatus, _text);

        await startup.CheckAsync();

        Assert.Equal(StartupPhase.GitUnavailable, startup.Phase);
        Assert.Empty(startup.CorruptFiles);
    }

    [Fact]
    public async Task A_check_that_fails_shows_its_error_with_a_way_to_try_again()
    {
        var backend = Backend(Healthy());
        backend.FailNext<IAppStatusApi>(nameof(IAppStatusApi.CheckAsync), ErrorInfo.From(new InvalidOperationException("boom")));
        var startup = new StartupViewModel(backend.AppStatus, _text);

        await startup.CheckAsync();

        Assert.Equal(StartupPhase.Failed, startup.Phase);
        Assert.Equal("Something went wrong", startup.Card!.Title);
        Assert.Equal(["Try again"], startup.Card.Actions.Select(action => action.Label));

        await startup.Card.Actions[0].Command.ExecuteAsync();

        Assert.Equal(StartupPhase.Ready, startup.Phase);
    }

    [Fact]
    public async Task A_check_that_cannot_succeed_on_a_second_try_asks_for_a_restart_instead_of_offering_one()
    {
        var failure = ErrorInfo.From(new IOException("The configuration folder cannot be created."));
        var startup = new StartupViewModel(AppBootstrap.Unavailable(failure).AppStatus, _text, canTryAgain: false);

        await startup.CheckAsync();

        Assert.Equal(StartupPhase.Failed, startup.Phase);
        Assert.Equal("SavePoint couldn't start", startup.Card!.Title);
        Assert.Contains("Close SavePoint and open it again", startup.Card.Body, StringComparison.Ordinal);
        Assert.Empty(startup.Card.Actions);
    }

    [Fact]
    public async Task The_composition_does_not_offer_a_retry_for_a_backend_that_never_started()
    {
        var failure = ErrorInfo.From(new IOException("The configuration folder cannot be created."));
        using var main = AppComposition.Create(AppBootstrap.Unavailable(failure), _text, new RecordingThemeApplier(), () => null, new ImmediateUiDispatcher(), new ManualTimeProvider(), new FakeDialogs());

        await main.Startup.CheckAsync();

        Assert.Empty(main.Startup.Card!.Actions);
    }

    [Fact]
    public async Task Checking_again_clears_what_the_last_check_showed()
    {
        var backend = Backend(WithCorrupt(Corrupt("games.json", null)));
        var startup = new StartupViewModel(backend.AppStatus, _text);
        await startup.CheckAsync();
        backend.State.Startup = Healthy();

        await startup.CheckAgainCommand.ExecuteAsync(null);

        Assert.Empty(startup.CorruptFiles);
        Assert.Equal(StartupPhase.Ready, startup.Phase);
    }

    private const string _storageRoot = @"F:\SavePoint";
    private const string _defaultStorageRoot = @"C:\Users\you\AppData\Local\SavePoint\Storage";
    private const string _refusedFolder = @"G:\Refused";

    // The Api of a screen whose storage choices are always refused with an error of the test's choosing.
    private sealed class RefusingStatus(StartupStatus status, ErrorInfo refusal) : IAppStatusApi
    {
        public Task<OperationResult<StartupStatus>> CheckAsync(CancellationToken cancellationToken = default, IProgress<StartupProgress>? progress = null) =>
            Task.FromResult(OperationResult.Success(status));

        public Task<OperationResult<string>> RecoverCatalogFileAsync(string path, CatalogRecovery recovery, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<OperationResult<string>> UseStorageFolderAsync(string folder, CancellationToken cancellationToken = default) =>
            Task.FromResult(OperationResult.Failure<string>(refusal));

        public Task<OperationResult<string>> StartWithEmptyStorageAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(OperationResult.Failure<string>(refusal));
    }

    private static ErrorInfo Refusal(string id)
    {
        SavepointException error = id switch
        {
            "error.storage.unreachable" => new StorageUnreachableException(_refusedFolder, null),
            "error.storage.stillReachable" => new StorageStillReachableException(_refusedFolder),
            "error.storage.noHistoryHere" => new StorageHistoryNotFoundException(_refusedFolder),
            "error.storage.defaultFolderHasHistory" => new StorageDefaultFolderHasHistoryException(_refusedFolder),
            _ => throw new ArgumentOutOfRangeException(nameof(id), id, null),
        };
        return ErrorInfo.From(error);
    }

    private static StartupStatus StorageGone() => Healthy() with
    {
        Storage = new StorageStatus(false, _storageRoot, ErrorInfo.From(new StorageUnreachableException(_storageRoot, null))),
    };

    [Fact]
    public async Task A_storage_folder_that_cannot_be_reached_names_it_and_offers_three_ways_on()
    {
        var startup = new StartupViewModel(Backend(StorageGone()).AppStatus, _text, dialogs: new FakeDialogs());
        var ready = 0;
        startup.Ready += (_, _) => ready++;

        await startup.CheckAsync();

        Assert.Equal(StartupPhase.StorageUnreachable, startup.Phase);
        Assert.Equal(0, ready);
        var card = startup.Card!;
        Assert.Equal("SavePoint can't reach your snapshots", card.Title);
        Assert.Equal(_storageRoot, card.Path);
        Assert.Equal(["Try again", "Find the folder", "Start with an empty folder"], card.Actions.Select(action => action.Label));
        Assert.True(card.Actions[0].IsPrimary);
        Assert.Contains("choose where it is now", card.Supplement as string, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_reachable_storage_folder_is_ready_and_one_that_was_not_checked_changes_nothing()
    {
        var startup = new StartupViewModel(Backend(Healthy() with { Storage = new StorageStatus(true, _storageRoot, null) }).AppStatus, _text);

        await startup.CheckAsync();

        Assert.Equal(StartupPhase.Ready, startup.Phase);
    }

    [Fact]
    public async Task Trying_again_gets_past_the_screen_once_the_drive_is_back()
    {
        var backend = Backend(StorageGone());
        var startup = new StartupViewModel(backend.AppStatus, _text, dialogs: new FakeDialogs());
        await startup.CheckAsync();
        Assert.Equal(StartupPhase.StorageUnreachable, startup.Phase);

        backend.State.Startup = Healthy();
        await startup.Card!.Actions[0].Command.ExecuteAsync();

        Assert.Equal(StartupPhase.Ready, startup.Phase);
    }

    [Fact]
    public async Task Finding_the_folder_asks_for_one_starting_at_the_old_path_and_points_SavePoint_at_it()
    {
        var backend = Backend(StorageGone(), @"G:\SavePoint");
        var dialogs = new FakeDialogs().AnswerFolder(@"G:\SavePoint");
        var startup = new StartupViewModel(backend.AppStatus, _text, dialogs: dialogs);
        await startup.CheckAsync();
        Assert.Equal(StartupPhase.StorageUnreachable, startup.Phase);

        await startup.Card!.Actions[1].Command.ExecuteAsync();

        var asked = Assert.Single(dialogs.FolderPicks);
        Assert.Equal("Choose the folder that holds your snapshots", asked.Title);
        Assert.Equal(_storageRoot, asked.StartPath);
        var used = Assert.Single(backend.Calls.To<IAppStatusApi>(nameof(IAppStatusApi.UseStorageFolderAsync)));
        Assert.Equal([@"G:\SavePoint"], used.Args);
        Assert.Equal(StartupPhase.Ready, startup.Phase);
    }

    [Fact]
    public async Task Cancelling_the_folder_picker_changes_nothing()
    {
        var backend = Backend(StorageGone());
        var startup = new StartupViewModel(backend.AppStatus, _text, dialogs: new FakeDialogs().AnswerFolder(null));
        await startup.CheckAsync();
        Assert.Equal(StartupPhase.StorageUnreachable, startup.Phase);

        await startup.Card!.Actions[1].Command.ExecuteAsync();

        Assert.Empty(backend.Calls.To<IAppStatusApi>(nameof(IAppStatusApi.UseStorageFolderAsync)));
        Assert.Equal(StartupPhase.StorageUnreachable, startup.Phase);
        Assert.Null(startup.StorageActionError);
    }

    [Fact]
    public async Task A_folder_with_no_saved_history_says_so_and_stays_on_the_screen()
    {
        var backend = Backend(StorageGone());
        var startup = new StartupViewModel(backend.AppStatus, _text, dialogs: new FakeDialogs().AnswerFolder(@"G:\Elsewhere"));
        await startup.CheckAsync();
        Assert.Equal(StartupPhase.StorageUnreachable, startup.Phase);

        await startup.Card!.Actions[1].Command.ExecuteAsync();

        Assert.Equal(StartupPhase.StorageUnreachable, startup.Phase);
        Assert.Contains("doesn't hold your snapshots", startup.StorageActionError, StringComparison.Ordinal);
        Assert.True(startup.HasStorageActionError);
        Assert.Equal(@"G:\Elsewhere", startup.StorageActionErrorPath);
    }

    [Fact]
    public async Task Starting_with_an_empty_folder_asks_first_and_names_the_old_path()
    {
        var backend = Backend(StorageGone());
        var dialogs = new FakeDialogs().AnswerConfirm(false);
        var startup = new StartupViewModel(backend.AppStatus, _text, dialogs: dialogs);
        await startup.CheckAsync();
        Assert.Equal(StartupPhase.StorageUnreachable, startup.Phase);

        await startup.Card!.Actions[2].Command.ExecuteAsync();

        var asked = Assert.Single(dialogs.Confirmations);
        Assert.Equal("Start with an empty folder?", asked.Title);
        Assert.Contains(_storageRoot, asked.Message, StringComparison.Ordinal);
        Assert.Equal("Start with an empty folder", asked.ConfirmLabel);
        Assert.True(asked.Destructive);
        Assert.Empty(backend.Calls.To<IAppStatusApi>(nameof(IAppStatusApi.StartWithEmptyStorageAsync)));
        Assert.Equal(StartupPhase.StorageUnreachable, startup.Phase);
    }

    [Fact]
    public async Task Confirming_starts_with_an_empty_folder_and_gets_past_the_screen()
    {
        var backend = Backend(StorageGone());
        var startup = new StartupViewModel(backend.AppStatus, _text, dialogs: new FakeDialogs().AnswerConfirm(true));
        await startup.CheckAsync();
        Assert.Equal(StartupPhase.StorageUnreachable, startup.Phase);

        await startup.Card!.Actions[2].Command.ExecuteAsync();

        Assert.Single(backend.Calls.To<IAppStatusApi>(nameof(IAppStatusApi.StartWithEmptyStorageAsync)));
        Assert.Equal(StartupPhase.Ready, startup.Phase);
    }

    [Fact]
    public async Task A_start_with_an_empty_folder_that_fails_says_why_and_stays_on_the_screen()
    {
        var backend = Backend(StorageGone(), _defaultStorageRoot);
        var startup = new StartupViewModel(backend.AppStatus, _text, dialogs: new FakeDialogs().AnswerConfirm(true));
        await startup.CheckAsync();
        Assert.Equal(StartupPhase.StorageUnreachable, startup.Phase);

        await startup.Card!.Actions[2].Command.ExecuteAsync();

        Assert.Equal(StartupPhase.StorageUnreachable, startup.Phase);
        Assert.Equal(
            "SavePoint can't start over in its default folder, shown below, because it already holds snapshots. To use them, choose \"Find the folder\" and pick it. To start over, move that folder elsewhere first; the snapshots go with it.",
            startup.StorageActionError);
        Assert.True(startup.HasStorageActionErrorPath);
        Assert.Equal(_defaultStorageRoot, startup.StorageActionErrorPath);
    }

    [Theory]
    [InlineData("error.storage.unreachable")]
    [InlineData("error.storage.stillReachable")]
    [InlineData("error.storage.noHistoryHere")]
    [InlineData("error.storage.defaultFolderHasHistory")]
    public async Task Either_choice_that_is_refused_with_a_folder_in_the_error_shows_that_folder(string id)
    {
        var dialogs = new FakeDialogs().AnswerFolder(@"G:\Anywhere").AnswerConfirm(true);
        var startup = new StartupViewModel(new RefusingStatus(StorageGone(), Refusal(id)), _text, dialogs: dialogs);
        await startup.CheckAsync();

        await startup.Card!.Actions[1].Command.ExecuteAsync();
        Assert.True(startup.HasStorageActionError);
        Assert.Equal(_refusedFolder, startup.StorageActionErrorPath);

        await startup.Card!.Actions[2].Command.ExecuteAsync();
        Assert.True(startup.HasStorageActionError);
        Assert.Equal(_refusedFolder, startup.StorageActionErrorPath);
    }

    [Fact]
    public async Task A_refusal_that_names_no_folder_shows_no_folder()
    {
        var unknown = new ErrorInfo("error.unknown", new Dictionary<string, object?>(), "boom");
        var startup = new StartupViewModel(new RefusingStatus(StorageGone(), unknown), _text, dialogs: new FakeDialogs().AnswerFolder(@"G:\Anywhere"));
        await startup.CheckAsync();

        await startup.Card!.Actions[1].Command.ExecuteAsync();

        Assert.True(startup.HasStorageActionError);
        Assert.Null(startup.StorageActionErrorPath);
        Assert.False(startup.HasStorageActionErrorPath);
    }

    [Fact]
    public async Task The_folder_of_a_refusal_goes_away_when_the_next_choice_starts()
    {
        var dialogs = new FakeDialogs().AnswerFolder(@"G:\Anywhere").AnswerFolder(null);
        var startup = new StartupViewModel(new RefusingStatus(StorageGone(), Refusal("error.storage.noHistoryHere")), _text, dialogs: dialogs);
        await startup.CheckAsync();
        await startup.Card!.Actions[1].Command.ExecuteAsync();
        Assert.Equal(_refusedFolder, startup.StorageActionErrorPath);

        await startup.Card!.Actions[1].Command.ExecuteAsync();

        Assert.False(startup.HasStorageActionError);
        Assert.Null(startup.StorageActionErrorPath);
    }

    [Fact]
    public async Task Trying_again_clears_the_folder_of_an_earlier_refusal()
    {
        var dialogs = new FakeDialogs().AnswerFolder(@"G:\Anywhere");
        var startup = new StartupViewModel(new RefusingStatus(StorageGone(), Refusal("error.storage.noHistoryHere")), _text, dialogs: dialogs);
        await startup.CheckAsync();
        await startup.Card!.Actions[1].Command.ExecuteAsync();
        Assert.Equal(_refusedFolder, startup.StorageActionErrorPath);

        await startup.Card!.Actions[0].Command.ExecuteAsync();

        Assert.False(startup.HasStorageActionError);
        Assert.Null(startup.StorageActionErrorPath);
    }

    [Fact]
    public async Task Without_dialogs_only_trying_again_is_offered_because_the_other_two_need_them()
    {
        var startup = new StartupViewModel(Backend(StorageGone()).AppStatus, _text);

        await startup.CheckAsync();
        Assert.Equal(StartupPhase.StorageUnreachable, startup.Phase);

        Assert.Equal(["Try again"], startup.Card!.Actions.Select(action => action.Label));
    }
}

internal static class CommandExtensions
{
    public static Task ExecuteAsync(this System.Windows.Input.ICommand command) =>
        command is CommunityToolkit.Mvvm.Input.IAsyncRelayCommand async ? async.ExecuteAsync(null) : Task.Run(() => command.Execute(null));
}
