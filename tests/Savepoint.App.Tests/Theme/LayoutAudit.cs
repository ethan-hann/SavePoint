using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
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

    // The library shelf is a strip of cards; one compact card row is enough to show something in a short window.
    private const double _shelfMinimumViewport = 56;

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
            if (control is ScrollViewer scroller && Scrolls(scroller) && scroller.Viewport.Height < (scroller.Name == "ShelfScroll" ? _shelfMinimumViewport : _minimumViewport) && scroller.FindAncestorOfType<TextBox>() is null && Contains(client, BoundsIn(window, scroller)))
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

    /// <summary>
    /// What spills sideways. Nothing in the app scrolls sideways, so a scroll area that has more to show across must be a bug, and any
    /// control or text that ends past the window or past the scroll area it sits in is cut off.
    /// </summary>
    public static IReadOnlyList<string> HorizontalProblems(Window window)
    {
        var problems = new List<string>();
        var client = new Rect(0, 0, window.ClientSize.Width, window.ClientSize.Height);

        foreach (var control in window.GetVisualDescendants().OfType<Control>())
        {
            if (!control.IsEffectivelyVisible || control.Opacity <= 0 || control.Bounds.Width <= 0 || control.Bounds.Height <= 0 || control.FindAncestorOfType<TextBox>() is not null)
            {
                continue;
            }

            if (control is ScrollViewer scroller && scroller.Extent.Width > scroller.Viewport.Width + 1)
            {
                problems.Add($"{Describe(scroller)} is {scroller.Extent.Width:0} px wide in a {scroller.Viewport.Width:0} px viewport");
                continue;
            }

            if (control is not (Button or TextBox or ComboBox or ListBoxItem or CheckBox or ToggleSwitch or TextBlock) || control.Name?.StartsWith("PART_", StringComparison.Ordinal) == true)
            {
                continue;
            }

            var bounds = BoundsIn(window, control);
            var limit = client;
            foreach (var scrollArea in control.GetVisualAncestors().OfType<ScrollViewer>())
            {
                var area = BoundsIn(window, scrollArea);
                limit = new Rect(Math.Max(limit.Left, area.Left), limit.Top, Math.Max(0, Math.Min(limit.Right, area.Right) - Math.Max(limit.Left, area.Left)), limit.Height);
            }

            if (bounds.Left < limit.Left - 1 || bounds.Right > limit.Right + 1)
            {
                problems.Add($"{Describe(control)}{TextOf(control)} spans {bounds.Left:0} to {bounds.Right:0} but its area spans {limit.Left:0} to {limit.Right:0}");
            }
            else if (control is TextBlock { TextWrapping: TextWrapping.NoWrap } text && text.TextTrimming == TextTrimming.None && NaturalWidth(text) is { } needed && needed > text.Bounds.Width - text.Padding.Left - text.Padding.Right + 1)
            {
                problems.Add($"{Describe(text)}{TextOf(text)} needs {needed:0} px and gets {text.Bounds.Width:0}");
            }
        }

        return problems;
    }

    // The width a line of plain text takes on one line, or null for text with runs, which the audit leaves to the bounds check.
    private static double? NaturalWidth(TextBlock block)
    {
        if (string.IsNullOrEmpty(block.Text))
        {
            return null;
        }

        var typeface = new Typeface(block.FontFamily, block.FontStyle, block.FontWeight, block.FontStretch);
        return new FormattedText(block.Text, System.Globalization.CultureInfo.InvariantCulture, block.FlowDirection, typeface, block.FontSize, null).WidthIncludingTrailingWhitespace;
    }

    private static string TextOf(Control control) => control is TextBlock { Text: { Length: > 0 } text } ? $" \"{(text.Length > 40 ? text[..40] : text)}\"" : string.Empty;

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
