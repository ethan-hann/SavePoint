using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Savepoint.App.Features.Common.Formatting;
using Savepoint.App.Features.Shell;
using Savepoint.App.Localization;
using Savepoint.App.Tests.Features.EndToEnd;
using Savepoint.App.Tests.Theme;
using Savepoint.Core.Api;

namespace Savepoint.App.Tests.Features.Shell;

public class StartupViewTests
{
    // A status check that never answers, which is how the checks look while they run.
    private sealed class NeverAnswers : IAppStatusApi
    {
        public Task<OperationResult<StartupStatus>> CheckAsync(CancellationToken cancellationToken = default) =>
            new TaskCompletionSource<OperationResult<StartupStatus>>().Task.WaitAsync(cancellationToken);

        public Task<OperationResult<string>> RecoverCatalogFileAsync(string path, CatalogRecovery recovery, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    [AvaloniaFact]
    public void While_the_checks_run_the_screen_says_so_in_words()
    {
        var text = new UiFormatter(LocalizationCatalog.LoadEmbedded("en", throwOnMissingKey: true), os: "windows", timeZone: TimeZoneInfo.Utc);
        var startup = new StartupViewModel(new NeverAnswers(), text);
        var view = new StartupView { DataContext = startup };
        using var host = new ThemeHost(view);

        _ = startup.CheckAsync();
        Dispatcher.UIThread.RunJobs();

        var texts = view.GetVisualDescendants().OfType<TextBlock>().Where(block => block.IsEffectivelyVisible).Select(block => block.Text).ToList();
        Assert.Contains("Checking your files\u2026", texts);
    }

    [AvaloniaFact]
    public async Task The_recovery_error_is_a_live_region_so_a_failed_choice_is_heard()
    {
        using var app = await AppStates.OpenAsync("startup-corrupt-catalog");

        var error = app.Find<Border>("RecoveryError");

        Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(error));
    }
}
