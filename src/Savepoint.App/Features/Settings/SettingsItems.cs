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

/// <summary>One accent swatch.</summary>
/// <param name="Accent">The accent it selects; its swatch is drawn in the preset's own seed color.</param>
/// <param name="Label">Its name ("Blue"), used as the swatch's accessible name.</param>
public sealed record AccentOption(AccentColorId Accent, string Label);
