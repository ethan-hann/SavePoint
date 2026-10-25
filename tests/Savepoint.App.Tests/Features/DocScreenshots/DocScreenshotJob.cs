using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using Savepoint.App.Features.Activity;
using Savepoint.App.Features.Editor;
using Savepoint.App.Features.Games;
using Savepoint.App.Features.Shell;
using Savepoint.App.Tests.Fakes;
using Savepoint.App.Tests.Features.EndToEnd;
using Savepoint.App.Tests.Features.Games;
using Savepoint.App.Tests.Theme;
using Savepoint.Core.Api;
using Savepoint.Core.Catalog;
using static Savepoint.App.Tests.Features.DocScreenshots.DocScreenshotSeed;

namespace Savepoint.App.Tests.Features.DocScreenshots;

// Draws the pictures in docs/images from the real views and view models over the fake backend: the light theme with the default accent, a
// fixed window and clock, and the seed of DocScreenshotSeed, so a rerun writes the same files. Each generator stages one screen and saves it.
internal static class DocScreenshotJob
{
    private const double _width = 1280;
    private const double _height = 800;

    private static readonly SortedDictionary<string, Func<string, Task>> _generators = new(StringComparer.Ordinal)
    {
        ["activity.png"] = Activity,
        ["add-game.png"] = AddGame,
        ["after-play.png"] = AfterPlay,
        ["before-playing.png"] = BeforePlaying,
        ["first-run.png"] = FirstRun,
        ["first-snapshot.png"] = FirstSnapshot,
        ["games.png"] = Games,
        ["history.png"] = History,
        ["profiles.png"] = Profiles,
        ["recently-removed.png"] = RecentlyRemoved,
        ["settings.png"] = SettingsScreen,
    };

    /// <summary>The file names of the images the job draws.</summary>
    public static IReadOnlyList<string> Names { get; } = [.. _generators.Keys];

    /// <summary>The folder the images are written to.</summary>
    public static string ImageFolder => Path.Combine(ThemeTestSupport.RepoRoot(), "docs", "images");

    /// <summary>Draws every image into <see cref="ImageFolder"/>.</summary>
    public static async Task RunAsync()
    {
        Directory.CreateDirectory(ImageFolder);
        try
        {
            foreach (var (name, generate) in _generators)
            {
                await generate(Path.Combine(ImageFolder, name));
            }
        }
        finally
        {
            ThemeTestSupport.ResetApplicationTheme();
        }
    }

    // Reduced motion holds the busy bars still, so a picture does not depend on where an animation happens to be when it is taken.
    private static async Task<ComposedApp> StartAsync(ScenarioBuilder scenario)
    {
        var app = await ComposedApp.StartWithViewAsync(new FakeBackend(scenario), ThemePreference.Light, ShellLayout.Sidebar, _width, _height);

        // The window host switches motion on again when it opens, so the setting goes on afterwards.
        ((App)Application.Current!).Theme.ReduceMotion = true;
        app.Settle();
        return app;
    }

    // A page shows no keyboard focus ring, so focus is dropped before the picture is taken.
    private static void Draw(ComposedApp app, string path)
    {
        app.Settle();
        app.Window.Focusable = true;
        app.Window.Focus(NavigationMethod.Unspecified);
        app.RenderTo(path);
    }

    private static ProfileRowViewModel Row(ComposedApp app, string id) => app.Games.Profiles.Single(row => row.Id == id);

    private static async Task Games(string path)
    {
        using var app = await StartAsync(Full(withSurvivalProfile: true));

        Draw(app, path);
    }

    private static async Task Profiles(string path)
    {
        using var app = await StartAsync(Full());

        Draw(app, path);
    }

    private static async Task FirstRun(string path)
    {
        using var app = await StartAsync(NoGames());

        Draw(app, path);
    }

    private static async Task AddGame(string path)
    {
        using var app = await StartAsync(NoGames());
        app.Shell.AddGameCommand.Execute(null);
        app.Settle();
        var editor = Assert.IsType<GameEditorViewModel>(app.Shell.Content);
        editor.Name = "Grounded";
        editor.InstallPath = Install;
        editor.SavesPath = SavesRaw;
        editor.ExePath = "Grounded.exe";
        await editor.RefreshSuggestionsAsync();

        Draw(app, path);
    }

    private static async Task FirstSnapshot(string path)
    {
        using var app = await StartAsync(NoProfilesYet());
        _ = app.Games.NewProfileCommand.ExecuteAsync(null);
        var wizard = app.Dialog<NewProfileWizardViewModel>();
        wizard.Name = VanillaName;
        await wizard.ContinueCommand.ExecuteAsync(null).Soon();
        await wizard.ContinueCommand.ExecuteAsync(null).Soon();
        app.Backend.Jobs.ReportProgress(
            app.Backend.Jobs.LastStarted(JobKind.Capture).JobId,
            new JobProgress(60, 128, BytesDone: 9_300_000_000, BytesTotal: 12_100_000_000, CurrentPath: @"Maine\Content\Paks\pakchunk0-WindowsNoEditor.pak"));

        Draw(app, path);
    }

    private static async Task BeforePlaying(string path)
    {
        using var app = await StartAsync(Full());
        _ = Row(app, VanillaId).PlayCommand.ExecuteAsync(null);

        Draw(app, path);
    }

    private static async Task AfterPlay(string path)
    {
        using var app = await StartAsync(Full());
        await Row(app, ModdedId).PlayCommand.ExecuteAsync(null).Soon();
        app.Backend.Launch.ReportRunning(GameId, Now - TimeSpan.FromMinutes(134));
        app.Backend.Launch.ReportExited(GameId, new TimeSpan(2, 14, 0), 3);

        Draw(app, path);
    }

    private static async Task History(string path)
    {
        using var app = await StartAsync(Full());
        app.Shell.Navigate(NavDestination.History);

        Draw(app, path);
    }

    private static async Task RecentlyRemoved(string path)
    {
        using var app = await StartAsync(Full());
        app.Shell.Navigate(NavDestination.History);
        app.History.IsRemovedOpen = true;

        Draw(app, path);
    }

    private static async Task SettingsScreen(string path)
    {
        using var app = await StartAsync(Full());
        app.Shell.Navigate(NavDestination.Settings);

        Draw(app, path);
    }

    // Five finished jobs and one still running, the newest first; the newest finished one, a snapshot of Vanilla, is open with its details.
    private static async Task Activity(string path)
    {
        using var app = await StartAsync(Full(startAt: Now - TimeSpan.FromHours(2)));
        var backend = app.Backend;
        var jobs = backend.Jobs;

        foreach (var (name, minutes) in new[] { ("Before the Hedge Lab", 0), ("After the Ant Hill", 25), ("Mods updated for 1.4", 40), ("Before the Koi Pond", 30) })
        {
            backend.Clock.Advance(TimeSpan.FromMinutes(minutes));
            var id = FakeAssert.Ok(await backend.Profiles.CaptureAsync(GameId, ModdedId, name)).JobId;
            jobs.ReportStep(id, JobSteps.CaptureFolder, ("folder", RepoRole.External), ("path", Saves));
            jobs.ReportStep(id, JobSteps.CaptureFolderDone, ("folder", RepoRole.External), ("files", 3));
            backend.Clock.Advance(TimeSpan.FromSeconds(50));
            jobs.Complete(id);
        }

        backend.Clock.Advance(TimeSpan.FromMinutes(12));
        var first = FakeAssert.Ok(await backend.Profiles.CaptureAsync(GameId, VanillaId, VanillaName, [RepoRole.Install, RepoRole.External])).JobId;
        jobs.ReportStep(first, JobSteps.CaptureFolder, ("folder", RepoRole.Install), ("path", Install));
        foreach (var file in new[] { @"Engine\Binaries\ThirdParty\CEF3\Win64\Resources\cef.pak", @"Maine\Binaries\Win64\Maine-Win64-Shipping.exe", @"Maine\Content\Paks\pakchunk0-WindowsNoEditor.pak" })
        {
            jobs.ReportProgress(first, new JobProgress(1, 128, CurrentPath: file));
        }

        jobs.ReportStep(first, JobSteps.CaptureFolderDone, ("folder", RepoRole.Install), ("files", 126));
        jobs.ReportStep(first, JobSteps.CaptureFolder, ("folder", RepoRole.External), ("path", Saves));
        jobs.ReportProgress(first, new JobProgress(127, 128, CurrentPath: @"Slot1\world.sav"));
        jobs.ReportStep(first, JobSteps.CaptureFolderDone, ("folder", RepoRole.External), ("files", 2));
        jobs.ReportProgress(first, new JobProgress(128, 128, BytesDone: 12_100_000_000, BytesTotal: 12_100_000_000));
        backend.Clock.Advance(TimeSpan.FromMinutes(2));
        jobs.Complete(first);

        backend.Clock.Advance(TimeSpan.FromMinutes(6));
        var running = FakeAssert.Ok(await backend.Profiles.CaptureAsync(GameId, ModdedId, "Session - 7-8-26")).JobId;
        jobs.ReportStep(running, JobSteps.CaptureFolder, ("folder", RepoRole.Install), ("path", Install));
        for (var i = 1; i <= 30; i++)
        {
            jobs.ReportProgress(running, new JobProgress(i, 120, BytesDone: i * 90_000_000L, BytesTotal: 10_800_000_000, CurrentPath: $@"Maine\Content\Paks\pakchunk{i:D2}-WindowsNoEditor.pak"));
        }

        var screen = app.Show<ActivityScreenViewModel>(NavDestination.Activity);
        screen.SelectedRow = screen.Rows[1];
        app.Toasts.DismissAll();

        // Choosing a row scrolls it into view; the picture starts at the top of the list, where the running job is.
        app.Settle();
        app.Find<ListBox>("JobList").GetVisualDescendants().OfType<ScrollViewer>().First().ScrollToHome();

        Draw(app, path);
    }
}
