namespace Savepoint.App.Theme;

// Resource key scheme for the palette: Md3<Role>Color holds the Color, Md3<Role>Brush the SolidColorBrush.
public static class ThemeKeys
{
    public const string Prefix = "Md3";

    public static string Color(string role) => $"{Prefix}{role}Color";

    public static string Brush(string role) => $"{Prefix}{role}Brush";

    // "onPrimaryContainer" -> "OnPrimaryContainer"; the palette's key-color roles drop their trailing "Color".
    public static string RoleName(string camelCaseRole)
    {
        var name = char.ToUpperInvariant(camelCaseRole[0]) + camelCaseRole[1..];
        return name.EndsWith("Color", StringComparison.Ordinal) ? name[..^"Color".Length] : name;
    }
}
