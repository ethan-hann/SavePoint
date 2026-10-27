using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Savepoint.App.Features.Common;
using Savepoint.App.Features.Common.Formatting;
using Savepoint.App.Features.Shell;
using Savepoint.App.Localization;
using Savepoint.App.Tests.Fakes;
using Savepoint.App.Tests.Features.EndToEnd;
using Savepoint.App.Tests.Theme;
using Savepoint.Core.Api;
using Savepoint.Core.Errors;

namespace Savepoint.App.Tests.Features.Shell;

public class StartupViewTests
{
    // A status check that never answers, which is how the checks look while they run.
    private sealed class NeverAnswers(StartupProgress? reports = null) : IAppStatusApi
    {
        public Task<OperationResult<StartupStatus>> CheckAsync(CancellationToken cancellationToken = default, IProgress<StartupProgress>? progress = null)
        {
            if (reports is { } step)
            {
                progress?.Report(step);
            }

            return new TaskCompletionSource<OperationResult<StartupStatus>>().Task.WaitAsync(cancellationToken);
        }

        public Task<OperationResult<string>> RecoverCatalogFileAsync(string path, CatalogRecovery recovery) =>
            throw new NotSupportedException();

        public Task<OperationResult<string>> UseStorageFolderAsync(string folder) =>
            throw new NotSupportedException();

        public Task<OperationResult<string>> StartWithEmptyStorageAsync() =>
            throw new NotSupportedException();
    }

    [AvaloniaFact]
    public void While_the_checks_run_the_screen_says_so_in_words()
    {
        var text = new UiFormatter(LocalizationCatalog.LoadEmbedded("en", throwOnMissingKey: true), os: "windows", timeZone: TimeZoneInfo.Utc);
        var startup = new StartupViewModel(new NeverAnswers(), text, new ImmediateUiDispatcher());
        var view = new StartupView { DataContext = startup };
        using var host = new ThemeHost(view);

        _ = startup.CheckAsync();
        Dispatcher.UIThread.RunJobs();

        var texts = view.GetVisualDescendants().OfType<TextBlock>().Where(block => block.IsEffectivelyVisible).Select(block => block.Text).ToList();
        Assert.Contains("Checking your files\u2026", texts);
    }

    [AvaloniaFact]
    public void While_an_earlier_cleanup_is_finished_the_screen_says_so_in_plain_words()
    {
        var text = new UiFormatter(LocalizationCatalog.LoadEmbedded("en", throwOnMissingKey: true), os: "windows", timeZone: TimeZoneInfo.Utc);
        var startup = new StartupViewModel(new NeverAnswers(StartupProgress.FinishingEarlierCleanup), text, new ImmediateUiDispatcher());
        var view = new StartupView { DataContext = startup };
        using var host = new ThemeHost(view);

        _ = startup.CheckAsync();
        Dispatcher.UIThread.RunJobs();

        var texts = view.GetVisualDescendants().OfType<TextBlock>().Where(block => block.IsEffectivelyVisible).Select(block => block.Text).ToList();
        Assert.Contains("Checking your files…", texts);
        Assert.Contains("SavePoint is finishing a cleanup it started earlier. This can take a few minutes, and your games are not touched meanwhile.", texts);
    }

    [AvaloniaFact]
    public void While_a_task_started_before_SavePoint_closed_is_waited_for_the_screen_says_so_in_plain_words()
    {
        var text = new UiFormatter(LocalizationCatalog.LoadEmbedded("en", throwOnMissingKey: true), os: "windows", timeZone: TimeZoneInfo.Utc);
        var startup = new StartupViewModel(new NeverAnswers(StartupProgress.WaitingForEarlierTask), text, new ImmediateUiDispatcher());
        var view = new StartupView { DataContext = startup };
        using var host = new ThemeHost(view);

        _ = startup.CheckAsync();
        Dispatcher.UIThread.RunJobs();

        var texts = view.GetVisualDescendants().OfType<TextBlock>().Where(block => block.IsEffectivelyVisible).Select(block => block.Text).ToList();
        Assert.Contains("SavePoint is waiting for a task it started before it closed. This can take a few seconds, and your games are not touched meanwhile.", texts);
        Assert.DoesNotContain(texts, line => line?.Contains("finishing a cleanup", StringComparison.Ordinal) == true);
    }

    [AvaloniaFact]
    public async Task The_recovery_error_is_a_live_region_so_a_failed_choice_is_heard()
    {
        using var app = await AppStates.OpenAsync("startup-corrupt-catalog");

        var error = app.Find<Border>("RecoveryError");

        Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(error));
    }

    [AvaloniaFact]
    public async Task The_unreachable_storage_screen_shows_the_path_and_a_button_for_each_way_on()
    {
        using var app = await AppStates.OpenAsync("startup-storage-unreachable");

        var texts = app.All<TextBlock>().Where(block => block.IsEffectivelyVisible).Select(block => block.Text).ToList();
        var buttons = app.All<Button>().Where(button => button.IsEffectivelyVisible).Select(button => button.Content as string ?? (button.Content as TextBlock)?.Text).ToList();

        Assert.Contains(@"F:\SavePoint", texts);
        Assert.Contains("Try again", buttons);
        Assert.Contains("Find the folder", buttons);
        Assert.Contains("Start with an empty folder", buttons);
    }

    [AvaloniaFact]
    public async Task The_storage_error_is_a_live_region_so_a_refused_folder_is_heard()
    {
        using var app = await AppStates.OpenAsync("startup-storage-unreachable");

        var error = app.Find<TextBlock>("StorageActionError");

        Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(error));
    }

    [AvaloniaFact]
    public async Task A_refused_start_shows_the_folder_it_names_where_it_can_be_selected_and_has_it_announced()
    {
        const string defaultRoot = @"C:\Users\you\AppData\Local\SavePoint\Storage";
        const string gone = @"F:\SavePoint";
        var text = new UiFormatter(LocalizationCatalog.LoadEmbedded("en", throwOnMissingKey: true), os: "windows", timeZone: TimeZoneInfo.Utc);
        var status = new StartupStatus(
            new GitStatus(true, @"C:\Program Files\SavePoint\git\cmd\git.exe", "2.56.0.windows.2", null),
            new CatalogStatus(@"C:\Users\you\AppData\Roaming\SavePoint", []),
            new StorageStatus(false, gone, ErrorInfo.From(new StorageUnreachableException(gone, null))));
        var backend = new FakeBackend(ScenarioBuilder.Sample().WithDefaultStorageRoot(defaultRoot).WithStartupStatus(status).WithStorageHistoryIn(defaultRoot));
        var startup = new StartupViewModel(backend.AppStatus, text, new ImmediateUiDispatcher(), dialogs: new FakeDialogs().AnswerConfirm(true));
        var view = new StartupView { DataContext = startup };
        using var host = new ThemeHost(view, width: 700, height: 900);
        await startup.CheckAsync();

        await startup.Card!.Actions[2].Command.ExecuteAsync();
        Dispatcher.UIThread.RunJobs();

        var selectable = view.GetVisualDescendants().OfType<SelectableTextBlock>().Where(block => block.IsEffectivelyVisible).ToList();
        Assert.Contains(defaultRoot, selectable.Select(block => block.Text));
        var announced = selectable.First(block => block.Text == defaultRoot).GetVisualAncestors().OfType<Control>()
            .FirstOrDefault(ancestor => AutomationProperties.GetLiveSetting(ancestor) == AutomationLiveSetting.Polite);
        Assert.NotNull(announced);
    }
}
