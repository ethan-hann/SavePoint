using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace Savepoint.App.Tests.Theme;

// What a person on a small or scaled screen can reach. A control that lies outside the window must sit in a scroll area that is itself
// fully in the window, and a scroll area that has more to show must be tall enough to show something.
internal static class LayoutAudit
{
    // The work areas D36 names, as the window's client size: 200% on a 1920x1080 screen is 960x540 and 150% on 1366x768 is 910x512,
    // less a 48px taskbar and a 32px title bar.
    public static readonly (string Name, double Width, double Height)[] ScaledScreens =
    [
        ("200% on 1080p", 960, 460),
        ("150% on 1366x768", 910, 432),
    ];

    // The tallest a scroll area may be squeezed to while it still has more to show.
    private const double _minimumViewport = 96;

    public static IReadOnlyList<string> Problems(Window window)
    {
        var problems = new List<string>();
        var client = new Rect(0, 0, window.ClientSize.Width, window.ClientSize.Height);

        foreach (var control in window.GetVisualDescendants().OfType<Control>())
        {
            if (!control.IsEffectivelyVisible || control.Bounds.Width <= 0 || control.Bounds.Height <= 0)
            {
                continue;
            }

            // A text box's own scroller is the field's business, not the page's.
            if (control is ScrollViewer scroller && Scrolls(scroller) && scroller.Viewport.Height < _minimumViewport && scroller.FindAncestorOfType<TextBox>() is null && Contains(client, BoundsIn(window, scroller)))
            {
                problems.Add($"{Describe(scroller)} has more to show but only {scroller.Viewport.Height:0} px to show it in");
            }

            if (!IsReachable(control) || !control.IsEffectivelyEnabled)
            {
                continue;
            }

            var bounds = BoundsIn(window, control);
            if (Contains(client, bounds))
            {
                continue;
            }

            var reachable = control.GetVisualAncestors().OfType<ScrollViewer>().Any(scroller => Scrolls(scroller) && Contains(client, BoundsIn(window, scroller)));
            if (!reachable)
            {
                problems.Add($"{Describe(control)} at {Format(bounds)} is outside the {client.Width:0}x{client.Height:0} window and no scroll area reaches it");
            }
        }

        return problems;
    }

    public static Rect BoundsIn(Visual root, Visual visual) =>
        new(visual.TranslatePoint(default, root) ?? default, visual.Bounds.Size);

    public static bool IsReachable(Control control) =>
        control is Button or TextBox or ComboBox or ListBoxItem or CheckBox or ToggleSwitch && control.Name?.StartsWith("PART_", StringComparison.Ordinal) != true;

    private static bool Scrolls(ScrollViewer scroller) =>
        scroller.Extent.Height > scroller.Viewport.Height + 1 || scroller.Extent.Width > scroller.Viewport.Width + 1;

    private static bool Contains(Rect outer, Rect inner) =>
        inner.Left >= outer.Left - 1 && inner.Top >= outer.Top - 1 && inner.Right <= outer.Right + 1 && inner.Bottom <= outer.Bottom + 1;

    private static string Format(Rect rect) => $"({rect.Left:0},{rect.Top:0} {rect.Width:0}x{rect.Height:0})";

    private static string Describe(Control control) =>
        $"{control.GetType().Name}{(string.IsNullOrEmpty(control.Name) ? string.Empty : $" #{control.Name}")} [{string.Join(' ', control.Classes.Where(c => !c.StartsWith(':')))}]";
}
