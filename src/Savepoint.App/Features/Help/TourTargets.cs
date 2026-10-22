using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using Savepoint.App.Features.Shell;

namespace Savepoint.App.Features.Help;

/// <summary>Finds the real control each tour step points at, in whichever layout the window is showing.</summary>
internal static class TourTargets
{
    /// <summary>
    /// The control a step points at, or null when it is not on screen. Profile and Play are in the first row of the profile table, New
    /// profile is the header's button, and History is the chip under the header (sidebar layout) or the History item of the top bar
    /// (library layout). Only controls that are showing count, so the layout that is hidden never answers.
    /// </summary>
    /// <param name="root">Where to look, usually the window.</param>
    /// <param name="step">What the tour is showing.</param>
    public static Control? Find(Visual root, TourStep step)
    {
        var showing = root.GetVisualDescendants().OfType<Control>().Where(control => control.IsEffectivelyVisible);
        return step switch
        {
            TourStep.Profile => InFirstRow(showing, "ProfileNameCell"),
            TourStep.Play => InFirstRow(showing, "PlayButton"),
            TourStep.History => showing.FirstOrDefault(control => control.Name == "HistoryChip") ?? showing.OfType<ListBoxItem>().FirstOrDefault(IsHistoryItemOfTopBar),
            _ => showing.FirstOrDefault(control => control.Name is "NewProfileButton" or "LibraryNewProfileButton"),
        };
    }

    private static Control? InFirstRow(IEnumerable<Control> showing, string name) =>
        showing.OfType<Border>().FirstOrDefault(border => border.Classes.Contains("table-row"))
            ?.GetVisualDescendants().OfType<Control>().FirstOrDefault(control => control.Name == name && control.IsEffectivelyVisible);

    private static bool IsHistoryItemOfTopBar(ListBoxItem item) =>
        item.DataContext is NavItemViewModel { Destination: NavDestination.History } && item.FindAncestorOfType<ListBox>()?.Name == "TopNavList";
}
