using Avalonia;
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.VisualTree;

namespace Savepoint.App.Tests.Theme;

// Screen tests call this on a rendered view: an icon-only button without an automation name is unusable by assistive tech.
internal static class AutomationAudit
{
    public static IReadOnlyList<Button> IconButtonsWithoutNames(Visual root) =>
        root.GetVisualDescendants()
            .OfType<Button>()
            .Where(button => button.Classes.Contains("icon") && string.IsNullOrWhiteSpace(AutomationProperties.GetName(button)))
            .ToList();

    // What assistive technology would be told for every control a person can act on or has to tell apart: one line per control that
    // has no name, a type name where a name belongs, or nothing but icon glyphs. Template parts and scroll bars are the toolkit's own.
    public static IReadOnlyList<string> Problems(Visual root)
    {
        var problems = new List<string>();
        foreach (var control in root.GetVisualDescendants().OfType<Control>())
        {
            if (!control.IsEffectivelyVisible || !NeedsName(control))
            {
                continue;
            }

            var name = NameOf(control);
            var where = $"{control.GetType().Name} {(string.IsNullOrEmpty(control.Name) ? string.Empty : $"#{control.Name} ")}[{string.Join(' ', control.Classes.Where(c => !c.StartsWith(':')))}]";
            if (string.IsNullOrWhiteSpace(name))
            {
                problems.Add($"{where} has no accessible name");
            }
            else if (name.Contains("Avalonia.", StringComparison.Ordinal))
            {
                problems.Add($"{where} is named after a type: '{name}'");
            }
            else if (name.All(IsGlyph))
            {
                problems.Add($"{where} is named by an icon glyph only");
            }
            else if (MissingLabel(control, name) is { } missing)
            {
                problems.Add($"{where} shows '{missing}' but is named '{name}'");
            }
        }

        return problems;
    }

    public static string? NameOf(Control control) => ControlAutomationPeer.CreatePeerForElement(control)?.GetName();

    // The control's visible label when its name leaves it out, or null. A person who says "click Browse" to a voice-control tool needs a
    // name that contains what the control shows (WCAG 2.5.3, Label in Name). The label is the first visible text; later lines are
    // descriptions. Case, spacing and a trailing ellipsis do not count. A switch keeps its label outside itself, so it is left out.
    private static string? MissingLabel(Control control, string name)
    {
        if (control is not (Button or ListBoxItem or MenuItem) || control is ToggleSwitch)
        {
            return null;
        }

        var label = control.GetVisualDescendants().OfType<TextBlock>().FirstOrDefault(block => block.IsEffectivelyVisible && Normalize(block.Text ?? string.Empty).Any(char.IsLetterOrDigit));
        return label is not null && !Normalize(name).Contains(Normalize(label.Text!), StringComparison.OrdinalIgnoreCase) ? label.Text!.Trim() : null;
    }

    private static string Normalize(string text) =>
        string.Join(' ', text.Replace(_ellipsis.ToString(), string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).TrimEnd('.', ' ');

    private const char _ellipsis = (char)0x2026;

    private static bool NeedsName(Control control)
    {
        if (control.Name?.StartsWith("PART_", StringComparison.Ordinal) == true || control.FindAncestorOfType<ScrollBar>() is not null)
        {
            return false;
        }

        return control is not RepeatButton and (Button or TextBox or ComboBox or ListBox or ListBoxItem or ProgressBar or MenuItem);
    }

    // Material Symbols are drawn from the private use area, so a name made only of them reads as nothing.
    private static bool IsGlyph(char c) => c is >= '' and <= '' || char.IsWhiteSpace(c);
}
