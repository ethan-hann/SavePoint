using Avalonia.Headless.XUnit;
using Savepoint.App.Theme;

namespace Savepoint.App.Tests.Theme.Fonts;

public class IconGlyphTests
{
    [AvaloniaTheory]
    [InlineData("SymbolsFont")]
    [InlineData("SymbolsFilledFont")]
    public void Every_icon_constant_has_a_glyph(string resourceKey)
    {
        var glyphTypeface = FontTestSupport.Resolve(FontTestSupport.Resource(resourceKey));

        var missing = FontTestSupport.AllIcons().Where(icon => !FontTestSupport.HasGlyph(glyphTypeface, icon.Value)).Select(icon => icon.Name);

        Assert.Empty(missing);
    }

    [AvaloniaTheory]
    [InlineData("SymbolsFont")]
    [InlineData("SymbolsFilledFont")]
    public void Fonts_carry_only_the_listed_icons(string resourceKey)
    {
        var glyphTypeface = FontTestSupport.Resolve(FontTestSupport.Resource(resourceKey));

        Assert.False(FontTestSupport.HasGlyph(glyphTypeface, FontTestSupport.UnlistedIcon));
    }

    [Fact]
    public void Icon_constants_are_single_private_use_code_points()
    {
        var icons = FontTestSupport.AllIcons();

        Assert.NotEmpty(icons);
        Assert.All(icons, icon => Assert.InRange(char.ConvertToUtf32(icon.Value, 0), 0xE000, 0xF8FF));
    }

    [Fact]
    public void Icon_constants_match_the_icon_list()
    {
        var listed = File.ReadAllLines(RepoFile("build/fonts/icons.txt"))
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith('#'))
            .Select(PascalCase)
            .Order(StringComparer.Ordinal);
        var generated = FontTestSupport.AllIcons().Select(icon => icon.Name).Order(StringComparer.Ordinal);

        Assert.Equal(listed, generated);
    }

    [Fact]
    public void Play_arrow_is_the_material_code_point()
    {
        Assert.Equal("", Icons.PlayArrow);
    }

    private static string PascalCase(string name) =>
        string.Concat(name.Split('_').Select(part => char.ToUpperInvariant(part[0]) + part[1..]));

    private static string RepoFile(string relativePath)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Savepoint.slnx")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return Path.Combine(directory.FullName, relativePath);
    }
}
