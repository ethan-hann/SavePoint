using Savepoint.App.Features.Common.Errors;
using Savepoint.App.Features.Common.Formatting;
using Savepoint.App.Features.Shell;
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

    private static FakeBackend Backend(StartupStatus status) => new(ScenarioBuilder.Mockup().WithStartupStatus(status));

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
        Assert.Contains("git exited with 127", card.Detail);
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
        Assert.Equal("SavePoint couldn't read its data", first.Title);
        Assert.Equal(["Restore the backup", "Start with nothing in it"], first.Actions.Select(action => action.Label));
        Assert.Equal(@"Last good copy: C:\Users\you\AppData\Roaming\SavePoint\games.json.bak", first.Supplement);
        Assert.Equal("There is no backup copy of this file.", startup.CorruptFiles[1].Card.Supplement);
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
    public async Task With_no_services_connected_the_screen_says_it_is_not_ready_and_never_becomes_ready()
    {
        var startup = new StartupViewModel(null, _text);
        var ready = 0;
        startup.Ready += (_, _) => ready++;

        await startup.CheckAsync();

        Assert.Equal(StartupPhase.Unavailable, startup.Phase);
        Assert.Equal("SavePoint isn't ready yet", startup.Card!.Title);
        Assert.Equal(ErrorTone.Neutral, startup.Card.Tone);
        Assert.Equal(0, ready);
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
}

internal static class CommandExtensions
{
    public static Task ExecuteAsync(this System.Windows.Input.ICommand command) =>
        command is CommunityToolkit.Mvvm.Input.IAsyncRelayCommand async ? async.ExecuteAsync(null) : Task.Run(() => command.Execute(null));
}
