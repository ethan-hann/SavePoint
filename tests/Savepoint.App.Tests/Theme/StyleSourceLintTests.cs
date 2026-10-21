using System.Text.RegularExpressions;
using Savepoint.App.Theme;

namespace Savepoint.App.Tests.Theme;

// Reads the style sources: colors come from roles, styles carry no text, and every resource a style names exists.
public partial class StyleSourceLintTests
{
    private static string StylesFolder => Path.Combine(ThemeTestSupport.RepoRoot(), "src", "Savepoint.App", "Styles");

    private static IEnumerable<string> StyleFiles() =>
        Directory.EnumerateFiles(StylesFolder, "*.axaml", SearchOption.AllDirectories);

    private static string Read(string path) => File.ReadAllText(path);

    private static string WithoutComments(string xaml) => CommentPattern().Replace(xaml, string.Empty);

    [GeneratedRegex(@"<!--.*?-->", RegexOptions.Singleline)]
    private static partial Regex CommentPattern();

    [GeneratedRegex(@"#[0-9A-Fa-f]{3,8}\b")]
    private static partial Regex HexPattern();

    [GeneratedRegex(@"\{(?:DynamicResource|StaticResource)\s+(?:ResourceKey=)?([A-Za-z0-9_]+)\s*\}")]
    private static partial Regex ReferencePattern();

    [GeneratedRegex(@"x:Key=""([^""{]+)""")]
    private static partial Regex KeyPattern();

    [GeneratedRegex(@"\b(?:Text|Content|Header|Title|Watermark|PlaceholderText|ToolTip\.Tip|AutomationProperties\.Name)=""[^""{]*[A-Za-z][^""]*""|Property=""(?:Text|Content|Header|Title|Watermark|PlaceholderText|ToolTip\.Tip|AutomationProperties\.Name)""\s+Value=""[^""{]*[A-Za-z][^""]*""")]
    private static partial Regex LiteralTextPattern();

    [Fact]
    public void Style_sources_exist()
    {
        Assert.True(Directory.Exists(StylesFolder));
        Assert.NotEmpty(StyleFiles());
    }

    [Fact]
    public void Styles_use_no_hex_colors_except_the_dialog_shadow_token()
    {
        foreach (var path in StyleFiles())
        {
            var hexes = HexPattern().Matches(WithoutComments(Read(path))).Select(m => m.Value.ToUpperInvariant());
            var allowed = Path.GetFileName(path) == "Tokens.axaml" ? new[] { "#80000000" } : [];
            Assert.True(!hexes.Except(allowed).Any(), $"{Path.GetFileName(path)} has a hex color: {string.Join(", ", hexes.Except(allowed))}");
        }
    }

    [Fact]
    public void Styles_carry_no_literal_text()
    {
        foreach (var path in StyleFiles())
        {
            var literals = LiteralTextPattern().Matches(WithoutComments(Read(path))).Select(m => m.Value).ToList();
            Assert.True(literals.Count == 0, $"{Path.GetFileName(path)} has literal text: {string.Join(" | ", literals)}");
        }
    }

    [Fact]
    public void Every_resource_a_style_names_is_defined()
    {
        var defined = new HashSet<string>(StyleFiles().SelectMany(path => KeyPattern().Matches(Read(path)).Select(m => m.Groups[1].Value)));
        var app = Read(Path.Combine(ThemeTestSupport.RepoRoot(), "src", "Savepoint.App", "App.axaml"));
        defined.UnionWith(KeyPattern().Matches(app).Select(m => m.Groups[1].Value));

        // Published by ThemeService for every accent and mode.
        foreach (var role in ThemeRoles.Build(AccentSeeds.For(Savepoint.Core.Catalog.AccentColorId.Blue), dark: true).Keys)
        {
            defined.Add(ThemeKeys.Color(role));
            defined.Add(ThemeKeys.Brush(role));
        }

        // Published by the service's motion resources.
        defined.UnionWith(["Md3TransitionsOpacityShort", "Md3TransitionsOpacityMedium", "Md3TransitionsKnob", "Md3EasingStandard", "Md3MotionDurationShort", "Md3MotionDurationMedium"]);

        // Fluent's own text-box flyout, which the text box theme keeps.
        defined.Add("DefaultTextBoxContextFlyout");

        foreach (var path in StyleFiles())
        {
            foreach (Match reference in ReferencePattern().Matches(WithoutComments(Read(path))))
            {
                var key = reference.Groups[1].Value;
                Assert.True(defined.Contains(key), $"{Path.GetFileName(path)} names '{key}', which nothing defines.");
            }
        }
    }

    [Fact]
    public void Every_color_a_style_sets_is_a_role_brush_or_transparent()
    {
        var colorSetters = new Regex(@"Property=""(?:Background|Foreground|BorderBrush|Fill|Stroke|TextElement\.Foreground|CaretBrush|SelectionBrush|SelectionForegroundBrush|PlaceholderForeground)""\s+Value=""([^""]+)""");
        foreach (var path in StyleFiles())
        {
            foreach (Match setter in colorSetters.Matches(WithoutComments(Read(path))))
            {
                var value = setter.Groups[1].Value;
                Assert.True(
                    value == "Transparent" || value.Contains("{DynamicResource Md3", StringComparison.Ordinal) || value.StartsWith("{TemplateBinding", StringComparison.Ordinal),
                    $"{Path.GetFileName(path)} sets a color to '{value}'.");
            }
        }
    }
}
