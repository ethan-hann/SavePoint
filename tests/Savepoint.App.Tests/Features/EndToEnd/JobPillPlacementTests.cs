using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Savepoint.App.Features.Activity;
using Savepoint.App.Features.Common.Toasts;
using Savepoint.App.Features.Games;
using Savepoint.App.Features.History;
using Savepoint.App.Features.Settings;
using Savepoint.App.Features.Shell;
using Savepoint.App.Tests.Fakes;
using Savepoint.App.Tests.Features.Activity;
using Savepoint.App.Tests.Theme;
using Savepoint.Core.Api;
using Savepoint.Core.Catalog;

namespace Savepoint.App.Tests.Features.EndToEnd;

// The job pill floats over the bottom left corner of the page's content while a job runs. Whatever a person needs to press must
// still take their clicks: on every screen, in both layouts, at the sizes the reflow and scaled-window tests use. A control outside
// a scroll area is tried as it stands; a control inside one is tried with the area scrolled to its end, because anywhere else it can
// be scrolled clear of the pill. The toasts have a row of their own, so the pill never lands on them either.
public class JobPillPlacementTests
{
    private static readonly (string Name, double Width, double Height)[] _sizes =
    [
        ("1040x660", 1040, 660),
        .. LayoutAudit.ScaledScreens,
        ("the smallest window", 640, 420),
    ];

    private static readonly string[] _screens =
    [
        "games",
        "games-banner",
        "games-running",
        "games-checking",
        "games-failure-card",
        "games-free-up",
        "history",
        "history-list",
        "history-removed",
        "history-removed-open",
        "activity",
        "settings",
        "how-it-works",
        "editor",
    ];

    public static TheoryData<string, string, string> ScreensAtEverySize()
    {
        var data = new TheoryData<string, string, string>();
        foreach (var layout in new[] { ShellLayout.Sidebar, ShellLayout.Library })
        {
            foreach (var screen in _screens)
            {
                foreach (var (size, _, _) in _sizes)
                {
                    data.Add(layout.ToString(), screen, size);
                }
            }
        }

        return data;
    }

    public static TheoryData<string, string> LayoutsAtEverySize()
    {
        var data = new TheoryData<string, string>();
        foreach (var layout in new[] { ShellLayout.Sidebar, ShellLayout.Library })
        {
            foreach (var (size, _, _) in _sizes)
            {
                data.Add(layout.ToString(), size);
            }
        }

        return data;
    }

    private static ProfileRowViewModel Row(ComposedApp app, string id) => app.Games.Profiles.Single(row => row.Id == id);

    // The screen the way a person gets there, with a job running: the pill is up and the screen has settled.
    private static async Task<ComposedApp> OpenAsync(string screen, ShellLayout layout, double width, double height, bool widestPill = true)
    {
        var scenario = screen.StartsWith("history-removed", StringComparison.Ordinal) ? RemovedSnapshotSamples.Scenario() : ScenarioBuilder.Mockup();
        var backend = new FakeBackend(scenario.WithSettings(MockupScenario.Settings with { Layout = layout }));
        if (screen == "activity")
        {
            await ActivityScenario.AddJobsAsync(backend);
        }

        var app = await ComposedApp.StartWithViewAsync(backend, layout: layout, width: width, height: height);
        switch (screen)
        {
            case "games-banner":
                _ = Row(app, MockupScenario.ModdedId).PlayCommand.ExecuteAsync(null);
                break;
            case "games-running":
                _ = Row(app, MockupScenario.ModdedId).PlayCommand.ExecuteAsync(null);
                app.Backend.Launch.ReportRunning(MockupScenario.SkyrimId, MockupScenario.RunningSince);
                break;
            case "games-checking":
                app.Backend.Launch.PreviewGate = token => new TaskCompletionSource().Task.WaitAsync(token);
                _ = Row(app, MockupScenario.ModdedId).PlayCommand.ExecuteAsync(null);
                break;
            case "games-failure-card":
                app.Backend.FailNext<ILaunchApi>(nameof(ILaunchApi.GetPreviewAsync), MockupScenario.FileInUseError);
                _ = Row(app, MockupScenario.ModdedId).PlayCommand.ExecuteAsync(null);
                break;
            case "history":
                app.Show<HistoryScreenViewModel>(NavDestination.History);
                break;
            case "history-list":
                app.Show<HistoryScreenViewModel>(NavDestination.History).SelectedViewOption = app.History.ViewOptions[1];
                break;
            case "history-removed":
                app.Show<HistoryScreenViewModel>(NavDestination.History);
                break;
            case "history-removed-open":
                app.Show<HistoryScreenViewModel>(NavDestination.History).IsRemovedOpen = true;
                break;
            case "activity":
                app.Show<ActivityScreenViewModel>(NavDestination.Activity);
                break;
            case "settings":
                app.Show<SettingsScreenViewModel>(NavDestination.Settings);
                break;
            case "how-it-works":
                app.Shell.ShowHelp();
                break;
            case "editor":
                app.Games.EditGameCommand.Execute(null);
                break;
        }

        app.Settle();
        if (screen == "games-free-up")
        {
            _ = app.Games.FreeUp.FreeUpCommand.ExecuteAsync(null);
        }
        else if (!app.Find<Button>("Pill").IsEffectivelyVisible)
        {
            // A snapshot, not a free-up: the Games footer's button is turned off while its own game frees up space, and this one leaves it on.
            FakeAssert.Ok(await app.Backend.Profiles.CaptureAsync(MockupScenario.SkyrimId, MockupScenario.ModdedId, "A snapshot"));
        }

        // A long game name makes the widest pill there can be, so that is the one the controls are tried against.
        if (widestPill)
        {
            app.Shell.JobStatus.PillText = "Taking a snapshot of The Elder Scrolls V: Skyrim Special Edition, Anniversary Edition with every mod and every patch";
        }

        app.Settle();
        Assert.True(app.Find<Button>("Pill").IsEffectivelyVisible, "The job pill is not up.");
        return app;
    }

    [AvaloniaTheory]
    [MemberData(nameof(ScreensAtEverySize))]
    public async Task No_control_under_the_job_pill_loses_its_clicks(string layoutName, string screen, string sizeName)
    {
        var (_, width, height) = _sizes.Single(size => size.Name == sizeName);
        using var app = await OpenAsync(screen, Enum.Parse<ShellLayout>(layoutName), width, height);

        Redraw(app);
        var problems = Covered(app, scrolledToEnd: false).Select(problem => $"{problem} (pinned)").ToList();
        foreach (var scroller in app.Window.GetVisualDescendants().OfType<ScrollViewer>().Where(Scrolls))
        {
            scroller.ScrollToEnd();
        }

        Redraw(app);
        problems.AddRange(Covered(app, scrolledToEnd: true).Select(problem => $"{problem} (scrolled to the end)"));

        Assert.True(problems.Count == 0, $"{screen}, {layoutName} layout, {sizeName} ({width}x{height}):{Environment.NewLine}{string.Join(Environment.NewLine, problems.Distinct())}");
    }

    [AvaloniaTheory]
    [InlineData("Sidebar", "games", "1040x660")]
    [InlineData("Sidebar", "games-banner", "150% on 1366x768")]
    [InlineData("Library", "games", "150% on 1366x768")]
    [InlineData("Sidebar", "settings", "the smallest window")]
    [InlineData("Library", "how-it-works", "200% on 1080p")]
    public async Task The_job_pill_renders_over_the_page_and_at_the_end_of_its_scroll(string layoutName, string screen, string sizeName)
    {
        var (_, width, height) = _sizes.Single(size => size.Name == sizeName);
        using var app = await OpenAsync(screen, Enum.Parse<ShellLayout>(layoutName), width, height, widestPill: false);
        var name = $"{layoutName.ToLowerInvariant()}-{screen}-{width}x{height}";

        var top = app.Render("job-pill", name);
        foreach (var scroller in app.Window.GetVisualDescendants().OfType<ScrollViewer>().Where(Scrolls))
        {
            scroller.ScrollToEnd();
        }

        var end = app.Render("job-pill", $"{name}-end");

        Assert.True(new FileInfo(top).Length > 10_000 && new FileInfo(end).Length > 10_000, "A render looks empty.");
    }

    [AvaloniaTheory]
    [MemberData(nameof(LayoutsAtEverySize))]
    public async Task The_job_pill_and_the_toasts_never_cover_each_other(string layoutName, string sizeName)
    {
        var (_, width, height) = _sizes.Single(size => size.Name == sizeName);
        using var app = await OpenAsync("games", Enum.Parse<ShellLayout>(layoutName), width, height);

        app.Toasts.Show(new ToastMessage("Could not save Skyrim Special Edition.", ToastKind.Error, "There is not enough free disk space to save this profile right now."));
        app.Toasts.Show(new ToastMessage("Only part of Satisfactory was saved.", ToastKind.Warning, "A file is in use."));
        app.Settle();

        var pill = LayoutAudit.BoundsIn(app.Window, app.Find<Button>("Pill"));
        var strip = LayoutAudit.BoundsIn(app.Window, app.Find<StackPanel>("Strip"));
        var toasts = app.Find<ItemsControl>("ToastList").GetVisualDescendants().OfType<Border>().Where(border => border.Classes.Contains("panel")).ToList();

        Assert.Equal(2, toasts.Count);
        Assert.False(pill.Intersects(strip), $"The pill at {pill} overlaps the toast stack at {strip}.");
        Assert.All(toasts, toast => Assert.False(pill.Intersects(LayoutAudit.BoundsIn(app.Window, toast)), $"The pill at {pill} covers a toast at {LayoutAudit.BoundsIn(app.Window, toast)}."));
        Assert.True(new Rect(app.Window.ClientSize).Contains(pill), $"The pill at {pill} is not inside the {app.Window.ClientSize} window.");
    }

    // Hit testing follows the last frame that was drawn, not the layout, so a frame is drawn before the clicks are tried.
    private static void Redraw(ComposedApp app)
    {
        app.Settle();
        for (var frame = 0; frame < 2; frame++)
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
        }
    }

    private static bool Scrolls(ScrollViewer scroller) => JobPillAudit.Scrolls(scroller);

    private static IEnumerable<string> Covered(ComposedApp app, bool scrolledToEnd) =>
        JobPillAudit.Covered(app.Window, app.Find<Button>("Pill"), scrolledToEnd);
}
