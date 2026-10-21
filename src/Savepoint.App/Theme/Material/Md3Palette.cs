using System.Globalization;

namespace Savepoint.App.Theme.Material;

// Every MaterialDynamicColors role for one seed and mode, as ARGB. v1 reads a subset of these; the rest
// are kept so later screens never need a second derivation.
public sealed record Md3Palette
{
    public required uint SeedArgb { get; init; }

    public required bool IsDark { get; init; }

    public required uint PrimaryPaletteKeyColor { get; init; }
    public required uint SecondaryPaletteKeyColor { get; init; }
    public required uint TertiaryPaletteKeyColor { get; init; }
    public required uint NeutralPaletteKeyColor { get; init; }
    public required uint NeutralVariantPaletteKeyColor { get; init; }
    public required uint Background { get; init; }
    public required uint OnBackground { get; init; }
    public required uint Surface { get; init; }
    public required uint SurfaceDim { get; init; }
    public required uint SurfaceBright { get; init; }
    public required uint SurfaceContainerLowest { get; init; }
    public required uint SurfaceContainerLow { get; init; }
    public required uint SurfaceContainer { get; init; }
    public required uint SurfaceContainerHigh { get; init; }
    public required uint SurfaceContainerHighest { get; init; }
    public required uint OnSurface { get; init; }
    public required uint SurfaceVariant { get; init; }
    public required uint OnSurfaceVariant { get; init; }
    public required uint InverseSurface { get; init; }
    public required uint InverseOnSurface { get; init; }
    public required uint Outline { get; init; }
    public required uint OutlineVariant { get; init; }
    public required uint Shadow { get; init; }
    public required uint Scrim { get; init; }
    public required uint SurfaceTint { get; init; }
    public required uint Primary { get; init; }
    public required uint OnPrimary { get; init; }
    public required uint PrimaryContainer { get; init; }
    public required uint OnPrimaryContainer { get; init; }
    public required uint InversePrimary { get; init; }
    public required uint Secondary { get; init; }
    public required uint OnSecondary { get; init; }
    public required uint SecondaryContainer { get; init; }
    public required uint OnSecondaryContainer { get; init; }
    public required uint Tertiary { get; init; }
    public required uint OnTertiary { get; init; }
    public required uint TertiaryContainer { get; init; }
    public required uint OnTertiaryContainer { get; init; }
    public required uint Error { get; init; }
    public required uint OnError { get; init; }
    public required uint ErrorContainer { get; init; }
    public required uint OnErrorContainer { get; init; }
    public required uint PrimaryFixed { get; init; }
    public required uint PrimaryFixedDim { get; init; }
    public required uint OnPrimaryFixed { get; init; }
    public required uint OnPrimaryFixedVariant { get; init; }
    public required uint SecondaryFixed { get; init; }
    public required uint SecondaryFixedDim { get; init; }
    public required uint OnSecondaryFixed { get; init; }
    public required uint OnSecondaryFixedVariant { get; init; }
    public required uint TertiaryFixed { get; init; }
    public required uint TertiaryFixedDim { get; init; }
    public required uint OnTertiaryFixed { get; init; }
    public required uint OnTertiaryFixedVariant { get; init; }

    // The palette SavePoint v1 derived: tonal spot at standard contrast.
    public static Md3Palette Build(uint seedArgb, bool dark) =>
        FromScheme(new SchemeTonalSpot(Hct.FromInt(seedArgb), dark, 0.0));

    public static Md3Palette FromScheme(DynamicScheme scheme) => new()
    {
        SeedArgb = scheme.SourceColorArgb,
        IsDark = scheme.IsDark,
        PrimaryPaletteKeyColor = scheme.GetArgb(MaterialDynamicColors.PrimaryPaletteKeyColor),
        SecondaryPaletteKeyColor = scheme.GetArgb(MaterialDynamicColors.SecondaryPaletteKeyColor),
        TertiaryPaletteKeyColor = scheme.GetArgb(MaterialDynamicColors.TertiaryPaletteKeyColor),
        NeutralPaletteKeyColor = scheme.GetArgb(MaterialDynamicColors.NeutralPaletteKeyColor),
        NeutralVariantPaletteKeyColor = scheme.GetArgb(MaterialDynamicColors.NeutralVariantPaletteKeyColor),
        Background = scheme.GetArgb(MaterialDynamicColors.Background),
        OnBackground = scheme.GetArgb(MaterialDynamicColors.OnBackground),
        Surface = scheme.GetArgb(MaterialDynamicColors.Surface),
        SurfaceDim = scheme.GetArgb(MaterialDynamicColors.SurfaceDim),
        SurfaceBright = scheme.GetArgb(MaterialDynamicColors.SurfaceBright),
        SurfaceContainerLowest = scheme.GetArgb(MaterialDynamicColors.SurfaceContainerLowest),
        SurfaceContainerLow = scheme.GetArgb(MaterialDynamicColors.SurfaceContainerLow),
        SurfaceContainer = scheme.GetArgb(MaterialDynamicColors.SurfaceContainer),
        SurfaceContainerHigh = scheme.GetArgb(MaterialDynamicColors.SurfaceContainerHigh),
        SurfaceContainerHighest = scheme.GetArgb(MaterialDynamicColors.SurfaceContainerHighest),
        OnSurface = scheme.GetArgb(MaterialDynamicColors.OnSurface),
        SurfaceVariant = scheme.GetArgb(MaterialDynamicColors.SurfaceVariant),
        OnSurfaceVariant = scheme.GetArgb(MaterialDynamicColors.OnSurfaceVariant),
        InverseSurface = scheme.GetArgb(MaterialDynamicColors.InverseSurface),
        InverseOnSurface = scheme.GetArgb(MaterialDynamicColors.InverseOnSurface),
        Outline = scheme.GetArgb(MaterialDynamicColors.Outline),
        OutlineVariant = scheme.GetArgb(MaterialDynamicColors.OutlineVariant),
        Shadow = scheme.GetArgb(MaterialDynamicColors.Shadow),
        Scrim = scheme.GetArgb(MaterialDynamicColors.Scrim),
        SurfaceTint = scheme.GetArgb(MaterialDynamicColors.SurfaceTint),
        Primary = scheme.GetArgb(MaterialDynamicColors.Primary),
        OnPrimary = scheme.GetArgb(MaterialDynamicColors.OnPrimary),
        PrimaryContainer = scheme.GetArgb(MaterialDynamicColors.PrimaryContainer),
        OnPrimaryContainer = scheme.GetArgb(MaterialDynamicColors.OnPrimaryContainer),
        InversePrimary = scheme.GetArgb(MaterialDynamicColors.InversePrimary),
        Secondary = scheme.GetArgb(MaterialDynamicColors.Secondary),
        OnSecondary = scheme.GetArgb(MaterialDynamicColors.OnSecondary),
        SecondaryContainer = scheme.GetArgb(MaterialDynamicColors.SecondaryContainer),
        OnSecondaryContainer = scheme.GetArgb(MaterialDynamicColors.OnSecondaryContainer),
        Tertiary = scheme.GetArgb(MaterialDynamicColors.Tertiary),
        OnTertiary = scheme.GetArgb(MaterialDynamicColors.OnTertiary),
        TertiaryContainer = scheme.GetArgb(MaterialDynamicColors.TertiaryContainer),
        OnTertiaryContainer = scheme.GetArgb(MaterialDynamicColors.OnTertiaryContainer),
        Error = scheme.GetArgb(MaterialDynamicColors.Error),
        OnError = scheme.GetArgb(MaterialDynamicColors.OnError),
        ErrorContainer = scheme.GetArgb(MaterialDynamicColors.ErrorContainer),
        OnErrorContainer = scheme.GetArgb(MaterialDynamicColors.OnErrorContainer),
        PrimaryFixed = scheme.GetArgb(MaterialDynamicColors.PrimaryFixed),
        PrimaryFixedDim = scheme.GetArgb(MaterialDynamicColors.PrimaryFixedDim),
        OnPrimaryFixed = scheme.GetArgb(MaterialDynamicColors.OnPrimaryFixed),
        OnPrimaryFixedVariant = scheme.GetArgb(MaterialDynamicColors.OnPrimaryFixedVariant),
        SecondaryFixed = scheme.GetArgb(MaterialDynamicColors.SecondaryFixed),
        SecondaryFixedDim = scheme.GetArgb(MaterialDynamicColors.SecondaryFixedDim),
        OnSecondaryFixed = scheme.GetArgb(MaterialDynamicColors.OnSecondaryFixed),
        OnSecondaryFixedVariant = scheme.GetArgb(MaterialDynamicColors.OnSecondaryFixedVariant),
        TertiaryFixed = scheme.GetArgb(MaterialDynamicColors.TertiaryFixed),
        TertiaryFixedDim = scheme.GetArgb(MaterialDynamicColors.TertiaryFixedDim),
        OnTertiaryFixed = scheme.GetArgb(MaterialDynamicColors.OnTertiaryFixed),
        OnTertiaryFixedVariant = scheme.GetArgb(MaterialDynamicColors.OnTertiaryFixedVariant),
    };

    // "#RRGGBB": alpha is dropped, like upstream hexFromArgb.
    public static string ToHex(uint argb) => "#" + (argb & 0xFFFFFF).ToString("X6", CultureInfo.InvariantCulture);

    // "#AARRGGBB", the form Avalonia parses.
    public static string ToArgbHex(uint argb) => "#" + argb.ToString("X8", CultureInfo.InvariantCulture);

    // Reads "#RRGGBB" (opaque) or "#AARRGGBB", with or without the leading '#'.
    public static bool TryParseHex(string? text, out uint argb)
    {
        argb = 0;
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        var digits = text[0] == '#' ? text.AsSpan(1) : text.AsSpan();
        if (digits.Length is not (6 or 8))
        {
            return false;
        }

        foreach (var digit in digits)
        {
            if (!char.IsAsciiHexDigit(digit))
            {
                return false;
            }
        }

        var value = uint.Parse(digits, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
        argb = digits.Length == 6 ? 0xFF000000u | value : value;
        return true;
    }

    // Keys are the upstream camelCase role names, which is what the reference vectors use.
    public IReadOnlyDictionary<string, uint> ToRoleMap() => new Dictionary<string, uint>
    {
        ["primaryPaletteKeyColor"] = PrimaryPaletteKeyColor,
        ["secondaryPaletteKeyColor"] = SecondaryPaletteKeyColor,
        ["tertiaryPaletteKeyColor"] = TertiaryPaletteKeyColor,
        ["neutralPaletteKeyColor"] = NeutralPaletteKeyColor,
        ["neutralVariantPaletteKeyColor"] = NeutralVariantPaletteKeyColor,
        ["background"] = Background,
        ["onBackground"] = OnBackground,
        ["surface"] = Surface,
        ["surfaceDim"] = SurfaceDim,
        ["surfaceBright"] = SurfaceBright,
        ["surfaceContainerLowest"] = SurfaceContainerLowest,
        ["surfaceContainerLow"] = SurfaceContainerLow,
        ["surfaceContainer"] = SurfaceContainer,
        ["surfaceContainerHigh"] = SurfaceContainerHigh,
        ["surfaceContainerHighest"] = SurfaceContainerHighest,
        ["onSurface"] = OnSurface,
        ["surfaceVariant"] = SurfaceVariant,
        ["onSurfaceVariant"] = OnSurfaceVariant,
        ["inverseSurface"] = InverseSurface,
        ["inverseOnSurface"] = InverseOnSurface,
        ["outline"] = Outline,
        ["outlineVariant"] = OutlineVariant,
        ["shadow"] = Shadow,
        ["scrim"] = Scrim,
        ["surfaceTint"] = SurfaceTint,
        ["primary"] = Primary,
        ["onPrimary"] = OnPrimary,
        ["primaryContainer"] = PrimaryContainer,
        ["onPrimaryContainer"] = OnPrimaryContainer,
        ["inversePrimary"] = InversePrimary,
        ["secondary"] = Secondary,
        ["onSecondary"] = OnSecondary,
        ["secondaryContainer"] = SecondaryContainer,
        ["onSecondaryContainer"] = OnSecondaryContainer,
        ["tertiary"] = Tertiary,
        ["onTertiary"] = OnTertiary,
        ["tertiaryContainer"] = TertiaryContainer,
        ["onTertiaryContainer"] = OnTertiaryContainer,
        ["error"] = Error,
        ["onError"] = OnError,
        ["errorContainer"] = ErrorContainer,
        ["onErrorContainer"] = OnErrorContainer,
        ["primaryFixed"] = PrimaryFixed,
        ["primaryFixedDim"] = PrimaryFixedDim,
        ["onPrimaryFixed"] = OnPrimaryFixed,
        ["onPrimaryFixedVariant"] = OnPrimaryFixedVariant,
        ["secondaryFixed"] = SecondaryFixed,
        ["secondaryFixedDim"] = SecondaryFixedDim,
        ["onSecondaryFixed"] = OnSecondaryFixed,
        ["onSecondaryFixedVariant"] = OnSecondaryFixedVariant,
        ["tertiaryFixed"] = TertiaryFixed,
        ["tertiaryFixedDim"] = TertiaryFixedDim,
        ["onTertiaryFixed"] = OnTertiaryFixed,
        ["onTertiaryFixedVariant"] = OnTertiaryFixedVariant,
    };
}
