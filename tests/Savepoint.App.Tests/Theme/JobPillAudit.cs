using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;

namespace Savepoint.App.Tests.Theme;

// What the job pill takes clicks from. Kept beside LayoutAudit so its own tests can feed it a hand-built tree.
internal static class JobPillAudit
{
    public static bool Scrolls(ScrollViewer scroller) =>
        scroller.IsEffectivelyVisible && scroller.Extent.Height > scroller.Viewport.Height + 1;

    // Every control a person can press that the pill sits on: some point of the control's visible part, under the pill's bounds, takes the
    // click to the pill. A control in a scroll area is looked at only with the area at its end, since anywhere else it can be scrolled
    // clear of the pill; a control outside any scroll area is looked at as it stands. With the area at its end, the text it ends with
    // counts too: the last thing on a page is not to be left behind the pill when there is room to scroll it clear.
    public static IEnumerable<string> Covered(Window window, Control pill, bool scrolledToEnd)
    {
        var pillBounds = LayoutAudit.BoundsIn(window, pill);
        var client = new Rect(window.ClientSize);

        foreach (var control in window.GetVisualDescendants().OfType<Control>())
        {
            if (control == pill || !control.IsEffectivelyVisible || control.GetVisualAncestors().Contains(pill))
            {
                continue;
            }

            var pressable = LayoutAudit.IsReachable(control) && control.IsEffectivelyEnabled && control.IsHitTestVisible;
            var text = scrolledToEnd && IsReadableText(control);
            if (!pressable && !text)
            {
                continue;
            }

            if (control.GetVisualAncestors().OfType<ScrollViewer>().Any(Scrolls) != scrolledToEnd)
            {
                continue;
            }

            var overlap = VisibleBounds(window, control, client).Intersect(pillBounds);
            if (overlap.Width > 0 && overlap.Height > 0 && PointsIn(overlap).Any(point => TakesTheClick(window, pill, point)))
            {
                yield return $"{Describe(control)} at {LayoutAudit.BoundsIn(window, control)} is under the pill at {pillBounds}";
            }
        }
    }

    // Words a person reads in their own right, not the label of a control that is looked at as a whole.
    private static bool IsReadableText(Control control) =>
        control is TextBlock { Opacity: > 0 } block
        && (!string.IsNullOrWhiteSpace(block.Text) || block.Inlines?.Count > 0)
        && !block.GetVisualAncestors().OfType<Control>().Any(LayoutAudit.IsReachable);

    // The part of a control a person can see: what its scroll areas and clipping parents leave of it, inside the window.
    private static Rect VisibleBounds(Window window, Control control, Rect client)
    {
        var visible = LayoutAudit.BoundsIn(window, control).Intersect(client);
        foreach (var ancestor in control.GetVisualAncestors().OfType<Control>().Where(ancestor => ancestor is ScrollViewer || ancestor.ClipToBounds))
        {
            visible = visible.Intersect(LayoutAudit.BoundsIn(window, ancestor));
        }

        return visible;
    }

    // The middle of the area and points a pixel in from its corners, so a pill with rounded corners is still found.
    private static IEnumerable<Point> PointsIn(Rect area)
    {
        var inset = Math.Min(1, Math.Min(area.Width, area.Height) / 2);
        yield return area.Center;
        yield return new Point(area.Left + inset, area.Top + inset);
        yield return new Point(area.Right - inset, area.Top + inset);
        yield return new Point(area.Left + inset, area.Bottom - inset);
        yield return new Point(area.Right - inset, area.Bottom - inset);
    }

    private static bool TakesTheClick(Window window, Control pill, Point point) =>
        window.InputHitTest(point) is Visual hit && (hit == pill || hit.GetVisualAncestors().Contains(pill));

    private static string Describe(Control control)
    {
        if (control is TextBlock { Text: { Length: > 0 } words })
        {
            return $"Text '{(words.Length > 40 ? words[..40] : words)}'";
        }

        var name = AutomationProperties.GetName(control);
        var label = string.IsNullOrWhiteSpace(name) ? control.Name : name;
        return $"{control.GetType().Name} '{label}' [{string.Join(' ', control.Classes.Where(c => !c.StartsWith(':')))}]";
    }
}
