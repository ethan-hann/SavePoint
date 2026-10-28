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
// be scrolled clear of the pill. The toasts float over the bottom right corner and the pill stands above them, so the pill never lands on them either.
public class JobPillPlacementTests
{
    // Largest first: a reused composition is only ever shrunk, so every size is one a fresh open could also produce.
    private static readonly (string Name, double Width, double Height)[] _sizes = LargestFirst(
    [
        ("1040x660", 1040, 660),
        .. LayoutAudit.ScaledScreens,
        ("the smallest window", 640, 420),
    ]);

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

    // The screens that scroll something by themselves as they open (the error card into view, the selected row into view, a launch card that
    // scrolls with the profiles in a short window), at some of the sizes.
    private static readonly HashSet<string> _opensScrolled = ["games-failure-card", "activity", "games-banner", "games-running", "games-checking"];

    // The Games footer's free-up button is the job here, so there is no state of this screen without it to try the toasts against.
    private const string _noJobFreeScreen = "games-free-up";

    private static (string Name, double Width, double Height)[] LargestFirst((string Name, double Width, double Height)[] sizes) =>
        [.. sizes.OrderByDescending(size => size.Width).ThenByDescending(size => size.Height)];

    public static TheoryData<string, string> ScreensInBothLayouts()
    {
        var data = new TheoryData<string, string>();
        foreach (var layout in new[] { ShellLayout.Sidebar, ShellLayout.Library })
        {
            foreach (var screen in _screens)
            {
                data.Add(layout.ToString(), screen);
            }
        }

        return data;
    }

    private static ProfileRowViewModel Row(ComposedApp app, string id) => app.Games.Profiles.Single(row => row.Id == id);

    // The screen the way a person gets there, settled, before any job runs.
    private static async Task<ComposedApp> OpenScreenAsync(string screen, ShellLayout layout, double width, double height)
    {
        var scenario = screen.StartsWith("history-removed", StringComparison.Ordinal) ? RemovedSnapshotSamples.Scenario() : ScenarioBuilder.Sample();
        var backend = new FakeBackend(scenario.WithSettings(SampleScenario.Settings with { Layout = layout }));
        if (screen == "activity")
        {
            await ActivityScenario.AddJobsAsync(backend);
        }

        var app = await ComposedApp.StartWithViewAsync(backend, layout: layout, width: width, height: height);
        switch (screen)
        {
            case "games-banner":
                _ = Row(app, SampleScenario.ModdedId).PlayCommand.ExecuteAsync(null);
                break;
            case "games-running":
                _ = Row(app, SampleScenario.ModdedId).PlayCommand.ExecuteAsync(null);
                app.Backend.Launch.ReportRunning(SampleScenario.SkyrimId, SampleScenario.RunningSince);
                break;
            case "games-checking":
                app.Backend.Launch.PreviewGate = token => new TaskCompletionSource().Task.WaitAsync(token);
                _ = Row(app, SampleScenario.ModdedId).PlayCommand.ExecuteAsync(null);
                break;
            case "games-failure-card":
                app.Backend.FailNext<ILaunchApi>(nameof(ILaunchApi.GetPreviewAsync), SampleScenario.FileInUseError);
                _ = Row(app, SampleScenario.ModdedId).PlayCommand.ExecuteAsync(null);
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
        return app;
    }

    // A job running on the screen: the pill is up, as wide as it gets when asked.
    private static async Task StartJobAsync(ComposedApp app, string screen, bool widestPill)
    {
        if (screen == _noJobFreeScreen)
        {
            _ = app.Games.FreeUp.FreeUpCommand.ExecuteAsync(null);
        }
        else if (!app.Find<Button>("Pill").IsEffectivelyVisible)
        {
            // A snapshot, not a free-up: the Games footer's button is turned off while its own game frees up space, and this one leaves it on.
            FakeAssert.Ok(await app.Backend.Profiles.CaptureAsync(SampleScenario.SkyrimId, SampleScenario.ModdedId, "A snapshot"));
        }

        // A long game name makes the widest pill there can be, so that is the one the controls are tried against.
        if (widestPill)
        {
            app.Shell.JobStatus.PillText = "Taking a snapshot of The Elder Scrolls V: Skyrim Special Edition, Anniversary Edition with every mod and every patch";
        }

        app.Settle();
        Assert.True(app.Find<Button>("Pill").IsEffectivelyVisible, "The job pill is not up.");
    }

    // The screen the way a person gets there, with a job running: the pill is up and the screen has settled.
    private static async Task<ComposedApp> OpenAsync(string screen, ShellLayout layout, double width, double height, bool widestPill = true, bool withJob = true)
    {
        var app = await OpenScreenAsync(screen, layout, width, height);
        if (withJob)
        {
            await StartJobAsync(app, screen, widestPill);
        }

        return app;
    }

    // One audit of the screen with the pill up, one with the toasts up and one with both, then the full stack of toasts in the smallest window;
    // each configuration places content differently, so each is tried. A window with the job runs through the configurations at each size, and a
    // second window, opened without the job, tries the toasts alone, since a job once started cannot be taken back.
    [AvaloniaTheory]
    [MemberData(nameof(ScreensInBothLayouts))]
    public async Task No_control_under_the_pill_or_the_toasts_loses_its_clicks_and_no_pinned_text_is_hidden_under_them(string layoutName, string screen)
    {
        var problems = new List<string>();
        if (screen != _noJobFreeScreen)
        {
            problems.AddRange(await CollectingAsync("toasts alone", () => SweepAsync(layoutName, screen, withJob: false, ToastsAlone)));
        }

        problems.AddRange(await CollectingAsync("with the job", () => SweepAsync(layoutName, screen, withJob: true, WithTheJob)));

        Assert.True(problems.Count == 0, $"{screen}, {layoutName} layout:{Environment.NewLine}{string.Join(Environment.NewLine, problems)}");
    }

    // A sweep that cannot finish, such as a pill that never came up, is a problem like any other, so the problems the sweep before it found are still reported.
    private static async Task<List<string>> CollectingAsync(string sweep, Func<Task<List<string>>> run)
    {
        try
        {
            return await run();
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return [$"[{sweep}] the sweep stopped: {exception.GetType().Name}: {exception.Message}"];
        }
    }

    // The pill over the page and at the end of its scroll, in render-output/job-pill/, to look at by eye.
    [AvaloniaFact]
    public async Task Writes_the_job_pill_over_the_page_and_at_the_end_of_its_scroll_on_request()
    {
        RenderOutput.SkipUnlessRequested();
        foreach (var (layoutName, screen, sizeName) in new[]
        {
            ("Sidebar", "games", "1040x660"),
            ("Sidebar", "games-banner", "150% on 1366x768"),
            ("Library", "games", "150% on 1366x768"),
            ("Sidebar", "settings", "the smallest window"),
            ("Library", "how-it-works", "200% on 1080p"),
        })
        {
            var (_, width, height) = _sizes.Single(size => size.Name == sizeName);
            using var app = await OpenAsync(screen, Enum.Parse<ShellLayout>(layoutName), width, height, widestPill: false);
            var name = $"{layoutName.ToLowerInvariant()}-{screen}-{width}x{height}";

            app.Render("job-pill", name);
            foreach (var scroller in app.Window.GetVisualDescendants().OfType<ScrollViewer>().Where(Scrolls))
            {
                scroller.ScrollToEnd();
            }

            app.Render("job-pill", $"{name}-end");
        }
    }

    private enum ToastLoad
    {
        None,
        Two,
        Heavy,
    }

    private static void ShowToasts(ComposedApp app, ToastLoad load)
    {
        const string Long = "There is not enough free disk space to save this profile right now, and the game keeps its files open until it is closed, so nothing could be moved.";
        if (load == ToastLoad.None)
        {
            return;
        }

        app.Toasts.Show(new ToastMessage("Could not save Skyrim Special Edition.", ToastKind.Error, load == ToastLoad.Heavy ? Long : "There is not enough free disk space to save this profile right now."));
        app.Toasts.Show(new ToastMessage("Only part of Satisfactory was saved.", ToastKind.Warning, load == ToastLoad.Heavy ? Long : "A file is in use."));
        if (load == ToastLoad.Heavy)
        {
            app.Toasts.Show(new ToastMessage("Could not free up space for Grounded.", ToastKind.Error, Long));
            app.Toasts.Show(new ToastMessage("Could not switch to Vanilla.", ToastKind.Warning, Long));
        }

        app.Settle();
    }

    // What one window is audited for at one size: each configuration of the pill and the toasts, with its name and what it found.
    private delegate IEnumerable<(string Configuration, List<string> Found)> Configurations(ComposedApp app, ScrollMemory scrolls, bool smallest);

    // Puts the scroll areas back where a fresh open has them: at the top, or, for a screen that opens scrolled, where its own opening left them.
    private sealed class ScrollMemory(List<(ScrollViewer Scroller, Vector Offset)>? opened)
    {
        public bool OpensScrolled => opened is not null;

        public void Reset(ComposedApp app)
        {
            foreach (var scroller in app.Window.GetVisualDescendants().OfType<ScrollViewer>())
            {
                if (opened is null)
                {
                    scroller.ScrollToHome();
                }
                else if (opened.FirstOrDefault(saved => saved.Scroller == scroller) is { Scroller: not null } kept)
                {
                    scroller.Offset = kept.Offset;
                }
            }

            app.Settle();
        }
    }

    private static IEnumerable<(string, List<string>)> ToastsAlone(ComposedApp app, ScrollMemory scrolls, bool smallest)
    {
        yield return ("toasts only", Audit(app, scrolls, withJob: false, ToastLoad.Two));
    }

    private static IEnumerable<(string, List<string>)> WithTheJob(ComposedApp app, ScrollMemory scrolls, bool smallest)
    {
        yield return ("pill only", Audit(app, scrolls, withJob: true, ToastLoad.None));

        scrolls.Reset(app);
        ShowToasts(app, ToastLoad.Two);
        yield return ("pill and toasts", Audit(app, scrolls, withJob: true, ToastLoad.Two));

        // The stack at its full height: four toasts with long wrapped details and the dismiss-all button, the pill up.
        if (smallest)
        {
            scrolls.Reset(app);
            app.Toasts.DismissAll();
            ShowToasts(app, ToastLoad.Heavy);
            yield return ("pill and a full-height toast stack", Audit(app, scrolls, withJob: true, ToastLoad.Heavy));
        }

        scrolls.Reset(app);
        app.Toasts.DismissAll();
        app.Settle();
    }

    // One composition per screen steps through the sizes, from the largest down, so the window only shrinks. Before each size every scroll
    // area goes back to the top, which is where a fresh open has it; a screen that opens already scrolled (the one that scrolls its error
    // card or its selected row into view) is opened fresh at every size instead, so it keeps the scroll its own opening gave it.
    private static async Task<List<string>> SweepAsync(string layoutName, string screen, bool withJob, Configurations configurations)
    {
        var layout = Enum.Parse<ShellLayout>(layoutName);
        var problems = new List<string>();
        ComposedApp? app = null;
        ScrollMemory? scrolls = null;
        var openFreshEachTime = _opensScrolled.Contains(screen);
        try
        {
            foreach (var (sizeName, width, height) in _sizes)
            {
                if (app is null || openFreshEachTime)
                {
                    app?.Dispose();
                    app = await OpenAsync(screen, layout, width, height, withJob: withJob);
                    if (!withJob)
                    {
                        ShowToasts(app, ToastLoad.Two);
                    }

                    scrolls = new ScrollMemory(openFreshEachTime ? [.. app.Window.GetVisualDescendants().OfType<ScrollViewer>().Select(scroller => (scroller, scroller.Offset))] : null);
                }
                else
                {
                    scrolls!.Reset(app);
                    app.Window.Width = width;
                    app.Window.Height = height;
                    app.Settle();
                }

                var smallest = sizeName == _sizes[^1].Name;
                foreach (var (configuration, found) in configurations(app, scrolls!, smallest))
                {
                    problems.AddRange(found.Select(problem => $"{sizeName} ({width}x{height}), {configuration}: {problem}"));
                }
            }
        }
        finally
        {
            app?.Dispose();
        }

        return problems;
    }

    // Everything the window shows now is audited: what the pill and the toasts take clicks from or hide, and that they keep clear of each other.
    private static List<string> Audit(ComposedApp app, ScrollMemory scrolls, bool withJob, ToastLoad toasts)
    {
        var overlays = new List<Control>();
        var found = new List<string>();
        if (withJob)
        {
            if (!app.Find<Button>("Pill").IsEffectivelyVisible)
            {
                return ["The job pill is not up."];
            }

            overlays.Add(app.Find<Button>("Pill"));
        }

        if (toasts != ToastLoad.None)
        {
            if (!app.Find<Grid>("Strip").IsEffectivelyVisible)
            {
                return ["No toast is up."];
            }

            overlays.Add(app.Find<Grid>("Strip"));
        }

        Redraw(app);
        var moved = app.Window.GetVisualDescendants().OfType<ScrollViewer>().Where(scroller => scroller.Offset != default).ToList();
        if (!scrolls.OpensScrolled && moved.Count > 0)
        {
            found.Add($"a scroll area did not start at the top ({string.Join(", ", moved.Select(scroller => $"{scroller.Name ?? scroller.GetType().Name} at {scroller.Offset.Y}"))}); add the screen to {nameof(_opensScrolled)}.");
        }

        if (withJob && toasts == ToastLoad.Two)
        {
            found.AddRange(PillAndToastsApart(app));
        }

        found.AddRange(overlays.SelectMany(overlay => Covered(app, overlay, scrolledToEnd: false)).Select(problem => $"{problem} (pinned)"));
        found.AddRange(overlays.SelectMany(overlay => HiddenPinnedText(app, overlay)).Select(problem => $"{problem} (pinned text)"));
        foreach (var scroller in app.Window.GetVisualDescendants().OfType<ScrollViewer>().Where(Scrolls))
        {
            scroller.ScrollToEnd();
        }

        Redraw(app);
        found.AddRange(overlays.SelectMany(overlay => Covered(app, overlay, scrolledToEnd: true)).Select(problem => $"{problem} (scrolled to the end)"));
        return [.. found.Distinct()];
    }

    // The pill stands above the toasts and both stay inside the window.
    private static IEnumerable<string> PillAndToastsApart(ComposedApp app)
    {
        var pill = LayoutAudit.BoundsIn(app.Window, app.Find<Button>("Pill"));
        var strip = LayoutAudit.BoundsIn(app.Window, app.Find<Grid>("Strip"));
        var toasts = app.Find<ItemsControl>("ToastList").GetVisualDescendants().OfType<Border>().Where(border => border.Classes.Contains("panel")).ToList();

        if (toasts.Count != 2)
        {
            yield return $"{toasts.Count} toasts are up, not 2.";
        }

        if (pill.Intersects(strip))
        {
            yield return $"The pill at {pill} overlaps the toast stack at {strip}.";
        }

        foreach (var toast in toasts.Where(toast => pill.Intersects(LayoutAudit.BoundsIn(app.Window, toast))))
        {
            yield return $"The pill at {pill} covers a toast at {LayoutAudit.BoundsIn(app.Window, toast)}.";
        }

        if (!new Rect(app.Window.ClientSize).Contains(pill))
        {
            yield return $"The pill at {pill} is not inside the {app.Window.ClientSize} window.";
        }
    }

    // Words that stay put while the page scrolls (a pinned footer, a pinned card) never get the 64 px of room a scroll area keeps under
    // its last row, so none of their text may sit under the pill, at any size and in either layout.
    private static IEnumerable<string> HiddenPinnedText(ComposedApp app, Control pill)
    {
        var window = app.Window;
        var pillBounds = LayoutAudit.BoundsIn(window, pill);
        var client = new Rect(window.ClientSize);

        foreach (var block in window.GetVisualDescendants().OfType<TextBlock>())
        {
            var hasWords = !string.IsNullOrWhiteSpace(block.Text) || block.Inlines?.Count > 0;
            if (!hasWords || block.Opacity <= 0 || !block.IsEffectivelyVisible || block.GetVisualAncestors().Contains(pill))
            {
                continue;
            }

            // Text inside a scroll area that can move is the scroll test's business: it can be scrolled clear of the pill.
            if (block.GetVisualAncestors().OfType<ScrollViewer>().Any(Scrolls))
            {
                continue;
            }

            var visible = LayoutAudit.BoundsIn(window, block).Intersect(client);
            foreach (var ancestor in block.GetVisualAncestors().OfType<Control>().Where(ancestor => ancestor is ScrollViewer || ancestor.ClipToBounds))
            {
                visible = visible.Intersect(LayoutAudit.BoundsIn(window, ancestor));
            }

            var overlap = visible.Intersect(pillBounds);
            if (overlap.Width > 0 && overlap.Height > 0)
            {
                yield return $"Text '{(block.Text?.Length > 40 ? block.Text[..40] : block.Text)}' at {LayoutAudit.BoundsIn(window, block)} is under the pill at {pillBounds}";
            }
        }
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

    private static IEnumerable<string> Covered(ComposedApp app, Control overlay, bool scrolledToEnd) =>
        JobPillAudit.Covered(app.Window, overlay, scrolledToEnd);
}
