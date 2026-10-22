using Savepoint.App.Features.Common;
using Savepoint.App.Features.Common.Art;
using Savepoint.App.Features.Common.Dialogs;
using Savepoint.App.Features.Common.Formatting;
using Savepoint.App.Features.Common.Preferences;
using Savepoint.App.Features.Common.Toasts;
using Savepoint.App.Features.Shell;
using Savepoint.App.Localization;
using Savepoint.App.Tests.Fakes;
using Savepoint.App.Tests.Features.History;
using Savepoint.Core.Api;
using Savepoint.Core.Errors;

namespace Savepoint.App.Tests.Features.Shell;

public class MainWindowViewModelTests
{
    private static readonly UiFormatter _text = new(LocalizationCatalog.LoadEmbedded("en", throwOnMissingKey: true), os: "windows", timeZone: TimeZoneInfo.Utc);

    private static (MainWindowViewModel Main, FakeBackend Backend, List<ShellViewModel> Shells) Build(FakeBackend? backend = null)
    {
        backend ??= FakeBackend.Mockup();
        var shells = new List<ShellViewModel>();
        var toasts = new ToastHostViewModel(new ManualTimeProvider(), new ImmediateUiDispatcher());
        var dialogs = new DialogHostViewModel();
        var main = new MainWindowViewModel(
            new StartupViewModel(backend.AppStatus, _text),
            () =>
            {
                var shell = new ShellViewModel(new ShellDependencies(
                    backend.Games,
                    backend.Profiles,
                    backend.Settings,
                    new SettingsWriter(backend.Settings),
                    backend.Jobs,
                    _text,
                    new ImmediateUiDispatcher(),
                    new FakeDialogs(),
                    toasts,
                    new RecordingThemeApplier(),
                    StandInScreens.Complete([]),
                    new ArtworkProvider(backend.Artwork, new ImmediateUiDispatcher()),
                    new ScriptedLaunchFlow()));
                shells.Add(shell);
                return shell;
            },
            dialogs,
            toasts);
        return (main, backend, shells);
    }

    [Fact]
    public void The_window_starts_on_the_startup_screens()
    {
        var (main, _, shells) = Build();

        Assert.Same(main.Startup, main.Content);
        Assert.Null(main.Shell);
        Assert.Empty(shells);
    }

    [Fact]
    public async Task When_the_checks_pass_the_shell_is_built_loaded_and_shown()
    {
        var (main, _, shells) = Build();

        await main.InitializeAsync();

        var shell = Assert.Single(shells);
        Assert.Same(shell, main.Shell);
        Assert.Same(shell, main.Content);
        Assert.True(shell.IsLoaded);
        Assert.Equal(3, shell.Games.Count);
    }

    [Fact]
    public async Task While_a_check_fails_the_startup_screen_stays_and_no_shell_is_built()
    {
        var backend = FakeBackend.Mockup();
        backend.FailNext<IAppStatusApi>(nameof(IAppStatusApi.CheckAsync), ErrorInfo.From(new InvalidOperationException("boom")));
        var (main, _, shells) = Build(backend);

        await main.InitializeAsync();

        Assert.Same(main.Startup, main.Content);
        Assert.Empty(shells);
    }

    [Fact]
    public async Task Getting_past_a_problem_brings_the_shell_up()
    {
        var backend = FakeBackend.Mockup();
        backend.FailNext<IAppStatusApi>(nameof(IAppStatusApi.CheckAsync), ErrorInfo.From(new InvalidOperationException("boom")));
        var (main, _, shells) = Build(backend);
        await main.InitializeAsync();

        await main.Startup.CheckAgainCommand.ExecuteAsync(null);

        Assert.Same(main.Shell, main.Content);
        Assert.Single(shells);
    }

    [Fact]
    public async Task The_shell_is_built_only_once()
    {
        var (main, _, shells) = Build();
        await main.InitializeAsync();

        await main.Startup.CheckAgainCommand.ExecuteAsync(null);

        Assert.Single(shells);
    }

    [Fact]
    public async Task Content_changes_are_announced_when_the_shell_appears()
    {
        var (main, _, _) = Build();
        var changed = new List<string?>();
        main.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        await main.InitializeAsync();

        Assert.Contains(nameof(MainWindowViewModel.Content), changed);
    }

    [Fact]
    public void The_dialog_stack_and_toasts_are_shared_with_the_window()
    {
        var (main, _, _) = Build();

        Assert.NotNull(main.Dialogs);
        Assert.NotNull(main.Toasts);
    }
}
