namespace Savepoint.App.Theme;

/// <summary>
/// The Fluent resource keys read by the Fluent controls the app keeps (scroll bars, menus, tooltips), mapped to the
/// palette role each should follow. The theme service points them at the role brushes and colors, so those controls
/// pick up the accent and mode without being re-themed.
/// </summary>
public static class FluentBridge
{
    /// <summary>Fluent brush key to role name; each key resolves to that role's brush instance.</summary>
    public static IReadOnlyDictionary<string, string> Brushes { get; } = new Dictionary<string, string>
    {
        ["ToolTipBackground"] = "InverseSurface",
        ["ToolTipForeground"] = "InverseOnSurface",
        ["ToolTipBorderBrush"] = "InverseSurface",
        ["MenuFlyoutPresenterBackground"] = "SurfaceContainer",
        ["MenuFlyoutPresenterBorderBrush"] = "OutlineVariant",
        ["MenuFlyoutItemBackgroundPointerOver"] = "StateHover",
        ["MenuFlyoutItemBackgroundPressed"] = "StatePressed",
        ["MenuFlyoutItemForeground"] = "OnSurface",
        ["MenuFlyoutItemForegroundPointerOver"] = "OnSurface",
        ["MenuFlyoutItemForegroundPressed"] = "OnSurface",
        ["MenuFlyoutItemForegroundDisabled"] = "DisabledContent",
        ["MenuFlyoutSubItemChevron"] = "OnSurfaceVariant",
        ["MenuFlyoutSubItemChevronPointerOver"] = "OnSurfaceVariant",
        ["MenuFlyoutSubItemChevronPressed"] = "OnSurfaceVariant",
        ["MenuFlyoutSubItemChevronSubMenuOpened"] = "OnSurfaceVariant",
        ["MenuFlyoutSubItemChevronDisabled"] = "DisabledContent",
        ["MenuFlyoutItemKeyboardAcceleratorTextForeground"] = "OnSurfaceVariant",
        ["MenuFlyoutItemKeyboardAcceleratorTextForegroundPointerOver"] = "OnSurfaceVariant",
        ["MenuFlyoutItemKeyboardAcceleratorTextForegroundPressed"] = "OnSurfaceVariant",
        ["MenuFlyoutItemKeyboardAcceleratorTextForegroundDisabled"] = "DisabledContent",
        ["ScrollBarPanningThumbBackground"] = "ScrollThumb",
        ["ScrollBarThumbFillPointerOver"] = "ScrollThumbHover",
        ["ScrollBarThumbFillPressed"] = "ScrollThumbPressed",
        ["ScrollBarTrackFill"] = "StateHover",
        ["ScrollBarTrackFillPointerOver"] = "StateHover",
    };

    /// <summary>Fluent color key to role name, for the Fluent keys that hold a <c>Color</c> rather than a brush.</summary>
    public static IReadOnlyDictionary<string, string> Colors { get; } = new Dictionary<string, string>
    {
        ["ScrollBarThumbBackgroundColor"] = "ScrollThumb",
        ["ScrollBarPanningThumbBackgroundColor"] = "ScrollThumb",
    };
}
