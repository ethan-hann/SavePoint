using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
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
}
