namespace Savepoint.App.Theme;

// Fluent controls the app keeps (scrollbars, menus, tooltips) read their colors from these Fluent keys; pointing them at the palette makes them follow the accent and mode.
public static class FluentBridge
{
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

    public static IReadOnlyDictionary<string, string> Colors { get; } = new Dictionary<string, string>
    {
        ["ScrollBarThumbBackgroundColor"] = "ScrollThumb",
        ["ScrollBarPanningThumbBackgroundColor"] = "ScrollThumb",
    };
}
