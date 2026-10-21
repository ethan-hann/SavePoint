namespace Savepoint.App.Theme;

/// <summary>
/// Names of the palette resources the theme service publishes: for a role such as <c>OnPrimaryContainer</c>,
/// <c>Md3OnPrimaryContainerColor</c> holds the <c>Color</c> and <c>Md3OnPrimaryContainerBrush</c> the
/// <c>SolidColorBrush</c>. Views and styles bind the brush key with <c>DynamicResource</c>.
/// </summary>
public static class ThemeKeys
{
    /// <summary>Starts every palette resource key.</summary>
    public const string Prefix = "Md3";

    /// <summary>Key of the <c>Color</c> resource for a role name (for example <c>Primary</c>).</summary>
    public static string Color(string role) => $"{Prefix}{role}Color";

    /// <summary>Key of the <c>SolidColorBrush</c> resource for a role name; the same brush instance for the life of the service.</summary>
    public static string Brush(string role) => $"{Prefix}{role}Brush";

    /// <summary>
    /// Turns the camelCase role names of the Material port (<c>onPrimaryContainer</c>) into the PascalCase names used in
    /// keys. The palette's key-color roles end in "Color" already, so that suffix is dropped to keep keys readable.
    /// </summary>
    public static string RoleName(string camelCaseRole)
    {
        var name = char.ToUpperInvariant(camelCaseRole[0]) + camelCaseRole[1..];
        return name.EndsWith("Color", StringComparison.Ordinal) ? name[..^"Color".Length] : name;
    }
}
