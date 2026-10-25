using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Savepoint.App.Features.Common;
using Savepoint.App.Features.Common.Dialogs;
using Savepoint.App.Features.Common.Formatting;
using Savepoint.App.Features.Common.Toasts;
using Savepoint.App.Features.Shell;
using Savepoint.App.Localization;
using Savepoint.App.Tests.Fakes;
using Savepoint.App.Tests.Features.Common.Samples;
using Savepoint.App.Tests.Theme;
using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using Savepoint.Core.Errors;

namespace Savepoint.App.Tests.Features.Shell;

// Writes PNGs to render-output/shell/ (gitignored) for side-by-side review against the shell and first-run designs at 1040x660.
public class ShellRenderTests
{
    private static FakeBackend EmptyLibrary(ThemePreference theme) =>
        new(ScenarioBuilder.Empty().WithSettings(AppSettings.Defaults(@"D:\SavePoint") with { Theme = theme }));

    private static FakeBackend WithSettings(ThemePreference theme) =>
        new(ScenarioBuilder.Sample().WithSettings(SampleScenario.Settings with { Theme = theme }));

    private static void AssertRendered(string path) =>
        Assert.True(new FileInfo(path).Length > 10_000, "The render looks empty.");

    [AvaloniaFact]
    public async Task Sidebar_layout_dark_renders_to_a_png()
    {
        using var harness = await ShellViewHarness.StartAsync();

        AssertRendered(harness.Render("shell-sidebar-dark"));
    }

    [AvaloniaFact]
    public async Task Sidebar_layout_light_renders_to_a_png()
    {
        using var harness = await ShellViewHarness.StartAsync(WithSettings(ThemePreference.Light), ThemePreference.Light);

        AssertRendered(harness.Render("shell-sidebar-light"));
    }

    [AvaloniaFact]
    public async Task Library_layout_dark_renders_to_a_png()
    {
        using var harness = await ShellViewHarness.LibraryLayoutAsync();

        AssertRendered(harness.Render("shell-library-dark"));
    }

    [AvaloniaFact]
    public async Task First_run_dark_renders_to_a_png()
    {
        using var harness = await ShellViewHarness.StartAsync(EmptyLibrary(ThemePreference.Dark));

        AssertRendered(harness.Render("first-run-dark"));
    }

    [AvaloniaFact]
    public async Task First_run_light_renders_to_a_png()
    {
        using var harness = await ShellViewHarness.StartAsync(EmptyLibrary(ThemePreference.Light), ThemePreference.Light);

        AssertRendered(harness.Render("first-run-light"));
    }

    [AvaloniaFact]
    public async Task Collapsed_rail_light_renders_to_a_png()
    {
        var backend = new FakeBackend(ScenarioBuilder.Sample().WithSettings(SampleScenario.Settings with { Theme = ThemePreference.Light }));
        using var harness = await ShellViewHarness.StartAsync(backend, ThemePreference.Light, width: 760, height: 660);

        AssertRendered(harness.Render("shell-sidebar-collapsed-light"));
    }

    [AvaloniaFact]
    public async Task Collapsed_rail_dark_renders_to_a_png()
    {
        using var harness = await ShellViewHarness.StartAsync(width: 760, height: 660);

        AssertRendered(harness.Render("shell-sidebar-collapsed-dark"));
    }

    [AvaloniaFact]
    public async Task Job_pill_and_toasts_dark_render_to_a_png()
    {
        using var harness = await ShellViewHarness.StartAsync();
        FakeAssert.Ok(await harness.Backend.Profiles.CaptureAsync(SampleScenario.SkyrimId, SampleScenario.ModdedId, "A point"));
        var finished = FakeAssert.Ok(await harness.Backend.Games.OptimizeStorageAsync(SampleScenario.SatisfactoryId));
        harness.Backend.Jobs.Complete(finished.JobId);
        harness.Main.Toasts.Show(new ToastMessage("Something went wrong saving The Last Caretaker.", ToastKind.Error, "There's not enough free disk space to take this snapshot right now."));

        AssertRendered(harness.Render("shell-job-pill-and-toasts-dark"));
    }

    [AvaloniaFact]
    public async Task A_confirmation_over_the_shell_dark_renders_to_a_png()
    {
        using var harness = await ShellViewHarness.StartAsync();
        _ = harness.Main.Dialogs.ShowAsync(new ConfirmDialogViewModel(
            new ConfirmRequest("Delete Skyrim Special Edition?", "This removes Skyrim Special Edition from SavePoint, including every profile and all captured history. This can't be undone.", "Delete game", Destructive: true),
            "Cancel"));

        AssertRendered(harness.Render("dialog-confirm-over-shell-dark"));
    }

    [AvaloniaFact]
    public async Task Startup_git_unavailable_dark_renders_to_a_png()
    {
        var path = @"C:\Program Files\SavePoint\git\cmd\git.exe";
        var status = new StartupStatus(
            new GitStatus(false, path, null, ErrorInfo.From(new GitUnavailableException(path, "git exited with 127: not found", null))),
            new CatalogStatus(@"C:\Users\you\AppData\Roaming\SavePoint", []));
        using var window = StartupWindow(new FakeBackend(ScenarioBuilder.Sample().WithStartupStatus(status)), ThemePreference.Dark);
        await window.Main.InitializeAsync();

        AssertRendered(window.Render("startup-git-unavailable-dark"));
    }

    [AvaloniaFact]
    public async Task Startup_corrupt_catalog_dark_renders_to_a_png()
    {
        var directory = @"C:\Users\you\AppData\Roaming\SavePoint";
        var games = Path.Combine(directory, "games.json").Replace('/', '\\');
        var profiles = Path.Combine(directory, "profiles.json").Replace('/', '\\');
        var status = new StartupStatus(
            new GitStatus(true, @"C:\Program Files\SavePoint\git\cmd\git.exe", "2.56.0.windows.2", null),
            new CatalogStatus(
                directory,
                [
                    new CorruptCatalogFile(games, games + ".bak", ErrorInfo.From(new CatalogCorruptException(games, null))),
                    new CorruptCatalogFile(profiles, null, ErrorInfo.From(new CatalogCorruptException(profiles, null))),
                ]));
        using var window = StartupWindow(new FakeBackend(ScenarioBuilder.Sample().WithStartupStatus(status)), ThemePreference.Dark);
        await window.Main.InitializeAsync();

        AssertRendered(window.Render("startup-corrupt-catalog-dark"));
    }

    [AvaloniaFact]
    public void Error_card_grid_dark_renders_to_a_png()
    {
        var text = new UiFormatter(LocalizationCatalog.LoadEmbedded("en", throwOnMissingKey: true), os: "windows", timeZone: TimeZoneInfo.Utc);
        var gallery = new ErrorCardGallery { DataContext = new ErrorCardGalleryViewModel(text) };
        using var host = new ThemeHost(gallery, ThemePreference.Dark, width: 1040, height: 660);
        Dispatcher.UIThread.RunJobs();
        var path = ThemeTestSupport.SaveFrame(host.Window, Path.Combine(ShellViewHarness.OutputFolder, "error-states-dark.png"));

        AssertRendered(path);
    }

    private static StartupHarness StartupWindow(FakeBackend backend, ThemePreference theme) => new(backend, theme);

    // The startup screens only.
    private sealed class StartupHarness : IDisposable
    {
        private readonly ThemeHost _host;

        public StartupHarness(FakeBackend backend, ThemePreference theme)
        {
            var text = new UiFormatter(LocalizationCatalog.LoadEmbedded("en", throwOnMissingKey: true), os: "windows", timeZone: TimeZoneInfo.Utc);
            var services = new AppServices(backend.Games, backend.Profiles, backend.Launch, backend.Settings, backend.History, backend.Jobs, backend.AppStatus, backend.Artwork);
            Main = AppComposition.Create(
                services,
                text,
                new ThemeServiceApplier(((App)Avalonia.Application.Current!).Theme),
                () => null,
                new ImmediateUiDispatcher(),
                new ManualTimeProvider(),
                new FakeDialogs());
            _host = new ThemeHost(new MainView { DataContext = Main }, theme, width: 1040, height: 660);
            Dispatcher.UIThread.RunJobs();
        }

        public MainWindowViewModel Main { get; }

        public string Render(string fileName)
        {
            Dispatcher.UIThread.RunJobs();
            return ThemeTestSupport.SaveFrame(_host.Window, Path.Combine(ShellViewHarness.OutputFolder, fileName + ".png"));
        }

        public void Dispose()
        {
            Main.Dispose();
            _host.Dispose();
        }
    }
}
