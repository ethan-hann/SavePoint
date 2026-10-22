using System.Text.RegularExpressions;
using Savepoint.App.Theme;
using Savepoint.Core.Catalog;

namespace Savepoint.App.Tests.Theme;

// WCAG contrast of the colors the styles actually pair, computed from the generated roles for every accent in both modes.
// A pair belongs in a table only when some style or view draws it, so a later palette change cannot quietly drop below the bar.
public partial class RoleContrastTests
{
    private const double _textMinimum = 4.5;
    private const double _boundaryMinimum = 3.0;

    // Everything text can sit on: the page, cards, rail and fields, dialogs, chips and toasts, wells and selected rows.
    private static readonly string[] _surfaces =
    [
        "Surface", "Card", "SurfaceContainerLow", "SurfaceContainer", "SurfaceContainerHigh", "SurfaceContainerHighest", "Recessed", "SelectedRow",
    ];

    // The surfaces a boundary or a colored stroke is drawn against.
    private static readonly string[] _strokeSurfaces =
    [
        "Surface", "Card", "SurfaceContainerLow", "SurfaceContainerHigh", "SurfaceContainerHighest", "Recessed",
    ];

    private static readonly (string Foreground, string[] Backgrounds)[] _textPairs =
    [
        ("OnSurface", _surfaces),
        ("OnSurfaceVariant", _surfaces),
        ("SubtleText", _surfaces),
        ("Primary", ["Surface", "Card", "SurfaceContainerLow", "SurfaceContainerHigh", "SelectedRow"]),
        ("Error", ["Surface", "Card", "SurfaceContainerLow", "SurfaceContainerHigh"]),
        ("Warning", ["Surface", "Card", "SurfaceContainerLow", "SurfaceContainerHigh"]),
        ("Success", ["Surface", "Card", "SurfaceContainerLow", "SurfaceContainerHigh"]),
        ("OnPrimary", ["Primary"]),
        ("OnPrimaryContainer", ["PrimaryContainer"]),
        ("OnSecondaryContainer", ["SecondaryContainer"]),
        ("OnError", ["Error"]),
        ("OnErrorContainer", ["ErrorCalloutBackground"]),
        ("OnWarning", ["Warning"]),
        ("OnWarningContainer", ["WarningCalloutBackground"]),
        ("OnSuccess", ["Success"]),
        ("OnSuccessContainer", ["SuccessCalloutBackground"]),
        ("InverseOnSurface", ["InverseSurface"]),
    ];

    private static readonly (string Foreground, string[] Backgrounds)[] _boundaryPairs =
    [
        // A field's border, against the page, a card, a dialog and the field's own fill.
        ("FieldBorder", _strokeSurfaces),
        // The off switch: its outline and knob against the track.
        ("FieldBorder", ["SurfaceContainerHighest"]),
        // Focus rings, the selected-card outline, the on switch and the error outline.
        ("Primary", _strokeSurfaces),
        ("Error", _strokeSurfaces),
    ];

    [GeneratedRegex(@"Property=""(?:Foreground|PlaceholderForeground)""\s+Value=""\{DynamicResource Md3Outline(?:Variant)?Brush\}""|\b(?:Foreground|PlaceholderForeground)=""\{DynamicResource Md3Outline(?:Variant)?Brush\}""")]
    private static partial Regex OutlineAsTextPattern();

    [GeneratedRegex(@"<!--.*?-->", RegexOptions.Singleline)]
    private static partial Regex CommentPattern();

    [Fact]
    public void Text_pairs_reach_4_5_to_1_for_every_accent_in_both_modes()
    {
        var failures = Failures(_textPairs, _textMinimum);
        Assert.True(failures.Count == 0, "Text below 4.5:1 (worst accent shown):" + Environment.NewLine + string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Boundary_pairs_reach_3_to_1_for_every_accent_in_both_modes()
    {
        var failures = Failures(_boundaryPairs, _boundaryMinimum);
        Assert.True(failures.Count == 0, "Boundaries below 3:1 (worst accent shown):" + Environment.NewLine + string.Join(Environment.NewLine, failures));
    }

    [Fact]
    public void Every_color_in_the_tables_is_opaque()
    {
        foreach (var dark in new[] { false, true })
        {
            var roles = ThemeRoles.Build(AccentSeeds.For(AccentColorId.Blue), dark);
            foreach (var role in _textPairs.Concat(_boundaryPairs).SelectMany(pair => pair.Backgrounds.Append(pair.Foreground)).Distinct())
            {
                Assert.True(roles.TryGetValue(role, out var argb), $"No role '{role}'.");
                Assert.Equal(0xFFu, argb >> 24);
            }
        }
    }

    [Fact]
    public void Text_never_takes_its_color_from_the_outline_roles()
    {
        var root = ThemeTestSupport.RepoRoot();
        var offenders = new List<string>();
        foreach (var path in Directory.EnumerateFiles(Path.Combine(root, "src", "Savepoint.App"), "*.axaml", SearchOption.AllDirectories))
        {
            var source = CommentPattern().Replace(File.ReadAllText(path), string.Empty);
            if (OutlineAsTextPattern().IsMatch(source))
            {
                offenders.Add(Path.GetRelativePath(root, path));
            }
        }

        Assert.True(offenders.Count == 0, "Text colored from Outline or OutlineVariant (use SubtleText): " + string.Join(", ", offenders));
    }

    private static List<string> Failures((string Foreground, string[] Backgrounds)[] pairs, double minimum)
    {
        var worst = new Dictionary<string, (double Ratio, AccentColorId Accent)>();
        foreach (var accent in Enum.GetValues<AccentColorId>())
        {
            foreach (var dark in new[] { false, true })
            {
                var roles = ThemeRoles.Build(AccentSeeds.For(accent), dark);
                foreach (var (foreground, backgrounds) in pairs)
                {
                    foreach (var background in backgrounds)
                    {
                        var ratio = Wcag.Ratio(roles[foreground], roles[background]);
                        var key = $"{(dark ? "Dark" : "Light")}: {foreground} on {background}";
                        if (!worst.TryGetValue(key, out var seen) || ratio < seen.Ratio)
                        {
                            worst[key] = (ratio, accent);
                        }
                    }
                }
            }
        }

        return [.. worst.Where(pair => pair.Value.Ratio < minimum).Select(pair => $"  {pair.Key} = {pair.Value.Ratio:0.00} ({pair.Value.Accent}), needs {minimum}")];
    }

}
