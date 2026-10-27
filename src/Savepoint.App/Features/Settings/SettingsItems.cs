using CommunityToolkit.Mvvm.ComponentModel;
using Savepoint.Core.Catalog;

namespace Savepoint.App.Features.Settings;

/// <summary>One segment of the theme control.</summary>
/// <param name="Preference">The preference it selects.</param>
/// <param name="Label">Its name, already localized ("Match Windows", "Light", "Dark").</param>
public sealed record ThemeOption(ThemePreference Preference, string Label);

/// <summary>One of the two layout previews.</summary>
/// <param name="Layout">The layout it selects.</param>
/// <param name="Label">Its name ("Sidebar", "Library").</param>
/// <param name="Description">What it is good for ("Every game one click away").</param>
public sealed record LayoutChoice(ShellLayout Layout, string Label, string Description)
{
    /// <summary>True for the sidebar preview.</summary>
    public bool IsSidebar => Layout == ShellLayout.Sidebar;

    /// <summary>True for the library preview.</summary>
    public bool IsLibrary => Layout == ShellLayout.Library;
}

/// <summary>
/// One step of the interface-size control. A step the window's screen has no room for stays in the list, switched off, and says why in
/// its tooltip and its accessible name.
/// </summary>
public sealed partial class ScaleOption : ObservableObject
{
    private readonly string _reason;
    private readonly string _unavailableName;

    /// <summary>Whether the step can be chosen.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AccessibleName), nameof(Reason))]
    private bool _isAvailable = true;

    /// <param name="percent">How large the interface is drawn, 100 to 200.</param>
    /// <param name="label">Its name ("125%").</param>
    /// <param name="reason">Why the step is switched off ("Your screen is too small for this size.").</param>
    /// <param name="unavailableName">The accessible name while switched off: the label and the reason.</param>
    public ScaleOption(int percent, string label, string reason, string unavailableName)
    {
        Percent = percent;
        Label = label;
        _reason = reason;
        _unavailableName = unavailableName;
    }

    /// <summary>How large the interface is drawn, 100 to 200.</summary>
    public int Percent { get; }

    /// <summary>The step's name ("125%").</summary>
    public string Label { get; }

    /// <summary>The name a screen reader speaks: the label, and why the step is unavailable while it is.</summary>
    public string AccessibleName => IsAvailable ? Label : _unavailableName;

    /// <summary>Why the step is switched off; null while it can be chosen.</summary>
    public string? Reason => IsAvailable ? null : _reason;
}

/// <summary>One accent swatch.</summary>
/// <param name="Accent">The accent it selects; its swatch is drawn in the preset's own seed color.</param>
/// <param name="Label">Its name ("Blue"), used as the swatch's accessible name.</param>
public sealed record AccentOption(AccentColorId Accent, string Label);
