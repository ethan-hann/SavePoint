using System.Reflection;
using System.Security.Cryptography;
using Avalonia;
using Avalonia.Media;
using Avalonia.Platform;
using Savepoint.App.Theme;

namespace Savepoint.App.Tests.Theme.Fonts;

internal static class FontTestSupport
{
    public const string AssetFolder = "avares://SavePoint/Assets/Fonts";
    public const string SymbolsFamily = "Material Symbols Rounded";

    // account_circle: a real Material Symbols icon that icons.txt does not list.
    public const string UnlistedIcon = "";

    public static FontFamily Resource(string key)
    {
        var app = Application.Current ?? throw new InvalidOperationException("No application is running.");
        Assert.True(app.TryGetResource(key, app.ActualThemeVariant, out var value), $"App.axaml defines no resource '{key}'.");
        return Assert.IsType<FontFamily>(value);
    }

    public static GlyphTypeface Resolve(FontFamily family, FontWeight weight = FontWeight.Normal)
    {
        var typeface = new Typeface(family, FontStyle.Normal, weight);
        Assert.True(FontManager.Current.TryGetGlyphTypeface(typeface, out var glyphTypeface), $"No glyph typeface for {family} at weight {(int)weight}.");
        return glyphTypeface;
    }

    // Proves the face came from our embedded file and not from a font installed on the machine.
    public static string HashOfResolvedFile(GlyphTypeface glyphTypeface)
    {
        Assert.True(glyphTypeface.PlatformTypeface.TryGetStream(out var stream), "The resolved typeface exposes no font data.");
        if (stream.CanSeek)
        {
            stream.Position = 0;
        }

        return Convert.ToHexString(SHA256.HashData(stream));
    }

    public static string HashOfEmbeddedFile(string fileName)
    {
        var uri = new Uri($"{AssetFolder}/{fileName}");
        Assert.True(AssetLoader.Exists(uri), $"{uri} is not an embedded resource.");
        using var stream = AssetLoader.Open(uri);
        return Convert.ToHexString(SHA256.HashData(stream));
    }

    public static IReadOnlyList<(string Name, string Value)> AllIcons() =>
        typeof(Icons)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral)
            .Select(f => (f.Name, (string)f.GetRawConstantValue()!))
            .ToList();

    public static bool HasGlyph(GlyphTypeface glyphTypeface, string icon) =>
        glyphTypeface.CharacterToGlyphMap.TryGetGlyph(char.ConvertToUtf32(icon, 0), out var glyph) && glyph != 0;
}
