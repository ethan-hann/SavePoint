using Savepoint.App.Theme.Material;

namespace Savepoint.App.Theme;

/// <summary>
/// Every color role the UI can ask for. Besides the Material 3 roles from the tonal-spot scheme it holds roles the
/// spec needs that M3 does not define: warning and success (D22), muted callout fills and borders, and a few
/// translucent or derived surfaces. Names here are role names; <see cref="ThemeKeys"/> turns them into resource keys.
/// </summary>
public static class ThemeRoles
{
    // Seeds whose hue D22 uses for the roles M3 does not define.
    private const uint _warningHueSeed = 0xFFF9A825;
    private const uint _successHueSeed = 0xFF2E7D32;

    // Same hue M3 uses for its error palette.
    private const double _errorHue = 25.0;

    // Moderate chroma keeps success and warning calm next to M3's vivid error red.
    private const double _extendedChroma = 48.0;

    private const double _calloutFillChroma = 10.0;
    private const double _calloutBorderChroma = 20.0;

    /// <summary>
    /// Every role for a seed and mode, as role name to ARGB.
    /// <list type="bullet">
    /// <item>M3 roles: Primary, OnPrimaryContainer, Surface, SurfaceContainerHigh, Outline, Error and the rest of the port's palette.</item>
    /// <item>Warning and Success, each with On, Container and OnContainer, at the tones M3 uses for error.</item>
    /// <item>Error, Warning and Success CalloutBackground and CalloutBorder: a muted tint of the family hue.</item>
    /// <item>Translucent: ScrimOverlay (scrim at 45%), StateHover and StatePressed (on-surface at 8% and 12%), DisabledContainer (12%), DisabledContent (38%), ProgressTrack, ScrollThumb, ScrollThumbHover, ScrollThumbPressed.</item>
    /// <item>Opaque derived: SelectedRow (surface tinted toward primary) and Card (the card surface).</item>
    /// </list>
    /// </summary>
    public static IReadOnlyDictionary<string, uint> Build(uint seedArgb, bool dark)
    {
        var palette = Md3Palette.Build(seedArgb, dark);
        var roles = new Dictionary<string, uint>();

        foreach (var (key, argb) in palette.ToRoleMap())
        {
            roles[ThemeKeys.RoleName(key)] = argb;
        }

        AddFamily(roles, "Warning", _warningHueSeed, dark);
        AddFamily(roles, "Success", _successHueSeed, dark);

        AddCallout(roles, "Error", _errorHue, dark);
        AddCallout(roles, "Warning", Hct.FromInt(_warningHueSeed).Hue, dark);
        AddCallout(roles, "Success", Hct.FromInt(_successHueSeed).Hue, dark);

        roles["ScrimOverlay"] = WithOpacity(palette.Scrim, 0.45);
        roles["StateHover"] = WithOpacity(palette.OnSurface, 0.08);
        roles["StatePressed"] = WithOpacity(palette.OnSurface, 0.12);
        roles["DisabledContainer"] = WithOpacity(palette.OnSurface, 0.12);
        roles["DisabledContent"] = WithOpacity(palette.OnSurface, 0.38);
        roles["ProgressTrack"] = WithOpacity(palette.Primary, 0.16);
        roles["ScrollThumb"] = WithOpacity(palette.OnSurfaceVariant, 0.4);
        roles["ScrollThumbHover"] = WithOpacity(palette.OnSurfaceVariant, 0.6);
        roles["ScrollThumbPressed"] = WithOpacity(palette.OnSurfaceVariant, 0.8);

        // Opaque, so a selected row reads the same on the page, in a dialog and inside a card.
        roles["SelectedRow"] = Mix(palette.Surface, palette.Primary, dark ? 0.075 : 0.06);

        // The spec draws cards lighter than the page in light mode, where M3's container tones go darker.
        roles["Card"] = dark ? palette.SurfaceContainer : palette.SurfaceContainerLowest;

        return roles;
    }

    // Tones M3 uses for error: 40/100/90/10 in light, 80/20/30/90 in dark.
    private static void AddFamily(Dictionary<string, uint> roles, string name, uint hueSeed, bool dark)
    {
        var palette = TonalPalette.FromHueAndChroma(Hct.FromInt(hueSeed).Hue, _extendedChroma);
        roles[name] = palette.Tone(dark ? 80 : 40);
        roles[$"On{name}"] = palette.Tone(dark ? 20 : 100);
        roles[$"{name}Container"] = palette.Tone(dark ? 30 : 90);
        roles[$"On{name}Container"] = palette.Tone(dark ? 90 : 10);
    }

    // Callouts sit on dialogs and cards, so they get a muted tint of the family hue rather than the saturated container role.
    private static void AddCallout(Dictionary<string, uint> roles, string name, double hue, bool dark)
    {
        roles[$"{name}CalloutBackground"] = TonalPalette.FromHueAndChroma(hue, _calloutFillChroma).Tone(dark ? 19 : 94);
        roles[$"{name}CalloutBorder"] = TonalPalette.FromHueAndChroma(hue, _calloutBorderChroma).Tone(dark ? 33 : 80);
    }

    private static uint WithOpacity(uint argb, double opacity) =>
        ((uint)Math.Round(opacity * 255) << 24) | (argb & 0x00FFFFFF);

    private static uint Mix(uint from, uint toward, double amount)
    {
        static uint Channel(uint from, uint toward, int shift, double amount)
        {
            double start = (from >> shift) & 0xFF;
            double end = (toward >> shift) & 0xFF;
            return (uint)Math.Round(start + ((end - start) * amount)) << shift;
        }

        return 0xFF000000u | Channel(from, toward, 16, amount) | Channel(from, toward, 8, amount) | Channel(from, toward, 0, amount);
    }
}
