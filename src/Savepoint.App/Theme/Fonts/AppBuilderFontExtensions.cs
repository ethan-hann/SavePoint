using Avalonia;
using Avalonia.Media;

namespace Savepoint.App.Theme.Fonts;

public static class AppBuilderFontExtensions
{
    public const string DefaultFontFamilyName = "avares://SavePoint/Assets/Fonts#Roboto";

    // Shared by Program and the headless test host so both start with the same default font.
    public static AppBuilder UseSavepointFonts(this AppBuilder builder) =>
        builder.With(new FontManagerOptions { DefaultFamilyName = DefaultFontFamilyName });
}
