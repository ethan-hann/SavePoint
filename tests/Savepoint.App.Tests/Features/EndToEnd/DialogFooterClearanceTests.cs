using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;
using Savepoint.App.Features.Common.Dialogs;
using Savepoint.App.Features.Games;
using Savepoint.App.Features.Launch;
using Savepoint.App.Features.Shell;
using Savepoint.App.Tests.Theme;
using Savepoint.Core.Api;
using Savepoint.Core.Catalog;

namespace Savepoint.App.Tests.Features.EndToEnd;

// A dialog's buttons sit clear of its edge, with their focus ring included, however full the dialog is and however small the window is.
// The ring is drawn outside the button, so a button that is the right distance from the edge can still have its ring on the edge or
// cut off by the viewer that scrolls the dialog. Renders go to render-output/dialog-footers/.
public class DialogFooterClearanceTests
{
    // The air the design leaves between a dialog's edge and what a person sees of a control, its ring included.
    private const double _airAroundRing = 26;

    // The ring is a stroke that anti-aliases and lands on fractional device pixels, so it needs more than its own reach inside the viewer.
    private const double _ringSlack = 4;

    private static readonly string[] _extraStates =
    [
        "wizard-saving-long-names",
        "wizard-saved",
        "confirm",
        "confirm-typed-word",
        "confirm-long-text",
        "close-during-snapshot",
        "launch-checks",
    ];

    public static TheoryData<string, string, double, double> Cases()
    {
        var data = new TheoryData<string, string, double, double>();
        var sizes = new List<(string Name, double Width, double Height)> { ("standard", 1040, 660), ("small", 800, 560) };
        sizes.AddRange(LayoutAudit.ScaledScreens);
        foreach (var state in AppStates.Dialogs.Select(row => row.Data).Concat(["launch-progress", "launch-dialog-saving"]).Concat(_extraStates))
        {
            foreach (var (name, width, height) in sizes)
            {
                data.Add(state, name, width, height);
            }
        }

        return data;
    }

    [AvaloniaTheory]
    [MemberData(nameof(Cases))]
    public async Task Every_dialogs_buttons_and_their_focus_rings_clear_its_edge(string state, string size, double width, double height)
    {
        using var app = await OpenAsync(state);
        app.Window.Width = width;
        app.Window.Height = height;
        app.Settle();

        var chrome = app.Find<Border>("Chrome");
        var scroll = app.Find<ScrollViewer>("DialogScroll");
        if (scroll.Extent.Height > scroll.Viewport.Height + 1)
        {
            scroll.ScrollToEnd();
            app.Settle();
        }

        var chromeBounds = LayoutAudit.BoundsIn(app.Window, chrome);
        var viewport = LayoutAudit.BoundsIn(app.Window, scroll);
        var buttons = chrome.GetVisualDescendants().OfType<Button>()
            .Where(button => button is not RepeatButton && button.IsEffectivelyVisible && button.Name?.StartsWith("PART_", StringComparison.Ordinal) != true)
            .Where(button => !button.GetVisualAncestors().OfType<ScrollViewer>().Any(scroller => scroller.Name != "DialogScroll"))
            .Select(button => (Button: button, Bounds: LayoutAudit.BoundsIn(app.Window, button)))
            .Where(item => viewport.Intersects(item.Bounds))
            .ToList();
        var bottomRow = buttons.Count == 0 ? 0 : buttons.Max(item => item.Bounds.Bottom);

        var problems = new List<string>();
        foreach (var (button, bounds) in buttons)
        {
            var reach = RingReach(button);
            var ring = bounds.Inflate(reach);

            // A dialog that scrolls as a whole shows only part of its buttons at a time; the row at the bottom is the one that meets the edge.
            var scrolled = scroll.Extent.Height > scroll.Viewport.Height + 1;
            var edges = new (string Name, double Gap)[]
            {
                ("left", ring.Left - chromeBounds.Left),
                ("right", chromeBounds.Right - ring.Right),
                ("top", ring.Top - chromeBounds.Top),
                ("bottom", chromeBounds.Bottom - ring.Bottom),
            };
            foreach (var (name, gap) in edges)
            {
                if (scrolled && name != "bottom" && name != "left" && name != "right")
                {
                    continue;
                }

                if (scrolled && bounds.Bottom < bottomRow - 1 && name == "bottom")
                {
                    continue;
                }

                if (gap < _airAroundRing - 0.5)
                {
                    problems.Add($"{Label(button)} ring is {gap:0.#}px from the dialog's {name} edge, needs {_airAroundRing}px");
                }
            }

            var inside = new (string Name, double Gap)[]
            {
                ("left", ring.Left - viewport.Left),
                ("right", viewport.Right - ring.Right),
                ("bottom", viewport.Bottom - ring.Bottom),
            };
            foreach (var (name, gap) in inside)
            {
                if (gap < _ringSlack - 0.5)
                {
                    problems.Add($"{Label(button)} ring has {gap:0.#}px inside the scrolling viewer's {name} edge, needs {_ringSlack}px");
                }
            }
        }

        if (state is "wizard-saving-long-names" && size is "standard" or "small")
        {
            app.Render("dialog-footers", $"{state}-{size}");
        }

        Assert.True(problems.Count == 0, $"{state} at {size} ({width}x{height}): {string.Join("; ", problems)}");
    }

    public static TheoryData<string, string> RingCases()
    {
        var data = new TheoryData<string, string>();
        foreach (var state in AppStates.Dialogs.Select(row => row.Data).Concat(["launch-progress", "launch-dialog-saving"]).Concat(_extraStates))
        {
            data.Add(state, "dark");
            if (_extraStates.Contains(state))
            {
                data.Add(state, "light");
            }
        }

        return data;
    }

    // The ring is drawn outside the button, so any ancestor that clips to its own bounds cuts it off at the button's edge while every
    // geometric measure still looks right. This looks at the pixels: with each button focused by keyboard, the ring's color has to be
    // on screen just outside the middle of each of its four sides, over the whole dialog at the standard and the small window.
    [AvaloniaTheory]
    [MemberData(nameof(RingCases))]
    public async Task Every_dialogs_focused_button_draws_its_whole_focus_ring(string state, string mode)
    {
        using var app = await OpenAsync(state, mode == "light" ? ThemePreference.Light : ThemePreference.Dark);
        var problems = new List<string>();
        foreach (var (width, height) in new[] { (1040d, 660d), (800d, 560d) })
        {
            app.Window.Width = width;
            app.Window.Height = height;
            app.Settle();
            var chrome = app.Find<Border>("Chrome");
            var scroll = app.Find<ScrollViewer>("DialogScroll");
            var viewport = LayoutAudit.BoundsIn(app.Window, scroll);
            var buttons = chrome.GetVisualDescendants().OfType<Button>()
                .Where(button => button.GetType() == typeof(Button) && button.IsEffectivelyVisible && button.IsEffectivelyEnabled && button.Name?.StartsWith("PART_", StringComparison.Ordinal) != true)
                .Where(button => !button.GetVisualAncestors().OfType<ScrollViewer>().Any(scroller => scroller.Name != "DialogScroll"))
                .Where(button => viewport.Contains(LayoutAudit.BoundsIn(app.Window, button)))
                .ToList();
            Assert.True(app.Window.TryFindResource("Md3PrimaryBrush", app.Window.ActualThemeVariant, out var found));
            var ringColor = ((SolidColorBrush)found!).Color;

            foreach (var button in buttons)
            {
                button.Focus(NavigationMethod.Tab);
                app.Settle();
                var bounds = LayoutAudit.BoundsIn(app.Window, button);
                var reach = RingReach(button);
                using var frame = app.Window.CaptureRenderedFrame() ?? throw new InvalidOperationException("No frame was rendered.");
                var middle = (int)Math.Round(bounds.Center.X);
                var centre = (int)Math.Round(bounds.Center.Y);
                var spots = new (string Side, int X, int Y)[]
                {
                    ("bottom", middle, (int)Math.Round(bounds.Bottom + reach - 1)),
                    ("top", middle, (int)Math.Round(bounds.Top - reach)),
                    ("left", (int)Math.Round(bounds.Left - reach), centre),
                    ("right", (int)Math.Round(bounds.Right + reach - 1), centre),
                };
                foreach (var (side, x, y) in spots)
                {
                    var pixel = ThemeTestSupport.PixelAt(frame, x, y);
                    if (Math.Abs(pixel.R - ringColor.R) + Math.Abs(pixel.G - ringColor.G) + Math.Abs(pixel.B - ringColor.B) > 60)
                    {
                        problems.Add($"{Label(button)} at {width}x{height}: ring missing on the {side} ({pixel} at {x},{y}, ring {ringColor})");
                    }
                }

                if (button.Name is "StopSaveButton" or "CancelButton" && width == 1040)
                {
                    ThemeTestSupport.SaveFrame(frame, Path.Combine(ThemeTestSupport.RepoRoot(), "render-output", "dialog-footers", $"ring-{state}-{mode}-{button.Name}.png"));
                }
            }
        }

        Assert.True(problems.Count == 0, $"{state} ({mode}): {string.Join("; ", problems)}");
    }

    // How far the button's own focus ring reaches past it, read from its template so a restyled ring is measured, not assumed.
    private static double RingReach(Button button)
    {
        var ring = button.GetVisualDescendants().OfType<Border>().FirstOrDefault(part => part.Name == "PART_FocusRing");
        return ring is null ? 0 : -ring.Margin.Left;
    }

    private static string Label(Button button) => $"{button.Name ?? button.Content?.ToString()}";

    private static async Task<ComposedApp> OpenAsync(string state, ThemePreference theme = ThemePreference.Dark)
    {
        if (!_extraStates.Contains(state))
        {
            return await AppStates.OpenAsync(state);
        }

        var app = await ComposedApp.StartWithViewAsync(theme: theme);
        switch (state)
        {
            case "wizard-saving-long-names" or "wizard-saved":
                _ = app.Games.NewProfileCommand.ExecuteAsync(null);
                var wizard = app.Dialog<NewProfileWizardViewModel>();
                await wizard.ContinueCommand.ExecuteAsync(null);
                await wizard.ContinueCommand.ExecuteAsync(null);
                var job = app.Backend.Jobs.LastStarted(JobKind.Capture).JobId;
                if (state == "wizard-saved")
                {
                    app.Backend.Jobs.Complete(job);
                }
                else
                {
                    app.Backend.Jobs.ReportProgress(job, new JobProgress(1_845, 3_430, 9_300_000_000, 12_100_000_000, CurrentPath: @"Data\Textures\Architecture\Whiterun\WRBuilding05_with_a_really_long_name_that_goes_on_and_on_and_on_and_on_and_on_n.dds"));
                }

                break;
            case "confirm":
                _ = app.Main.Dialogs.ShowAsync(new ConfirmDialogViewModel(new ConfirmRequest("Remove this profile?", "It moves to Recently removed.", "Remove profile"), "Cancel"));
                break;
            case "confirm-typed-word":
                _ = app.Main.Dialogs.ShowAsync(new ConfirmDialogViewModel(
                    new ConfirmRequest("Clear all data?", "This deletes every snapshot and profile for good. This can't be undone.", "Delete everything", Destructive: true, Typed: new TypedConfirmation("DELETE", "Type DELETE to continue.")),
                    "Cancel"));
                break;
            case "confirm-long-text":
                _ = app.Main.Dialogs.ShowAsync(new ConfirmDialogViewModel(
                    new ConfirmRequest("Delete Skyrim Special Edition and everything saved for it?", string.Join(' ', Enumerable.Repeat("This removes the game from SavePoint, including every profile and all captured history, and frees the space they use.", 4)), "Delete game", Destructive: true),
                    "Cancel"));
                break;
            case "close-during-snapshot":
                _ = app.Main.Dialogs.ShowAsync(new CloseDuringSnapshotDialogViewModel(app.Text));
                break;
            case "launch-checks":
                _ = app.Main.Dialogs.ShowAsync(new LaunchChecksDialogViewModel(
                    [.. Enumerable.Range(1, 8).Select(i => new LaunchCheckLine("check", $"Everything about check {i} looks fine.", null))],
                    app.Text));
                break;
        }

        app.Settle();
        return app;
    }
}
