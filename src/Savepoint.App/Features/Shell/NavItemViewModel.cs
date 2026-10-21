using CommunityToolkit.Mvvm.ComponentModel;

namespace Savepoint.App.Features.Shell;

/// <summary>One destination in the navigation: Games, History or Settings.</summary>
public sealed partial class NavItemViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ToolTipText))]
    private bool _showLabel = true;

    /// <summary>False while the destination has nothing to show: History is dimmed until the library has a game.</summary>
    [ObservableProperty]
    private bool _isEnabled = true;

    /// <param name="destination">Where the item goes.</param>
    /// <param name="label">Its name, already localized.</param>
    /// <param name="icon">A Material Symbols glyph.</param>
    public NavItemViewModel(NavDestination destination, string label, string icon)
    {
        Destination = destination;
        Label = label;
        Icon = icon;
    }

    /// <summary>Where the item goes.</summary>
    public NavDestination Destination { get; }

    /// <summary>The item's name; also its accessible name when the rail shows icons only.</summary>
    public string Label { get; }

    /// <summary>The Material Symbols glyph.</summary>
    public string Icon { get; }

    /// <summary>The tooltip shown when the rail is collapsed to icons and the name is hidden; null while the name is visible.</summary>
    public string? ToolTipText => ShowLabel ? null : Label;
}
