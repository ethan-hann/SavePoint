// Copyright 2022 Google LLC
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//      http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.
//
// Modified: translated to C# from dynamiccolor/material_dynamic_colors.js in @material/material-color-utilities 0.3.0.
// The color names stay in upstream snake_case: ToneDeltaPair resolves which role is nearer by name.

// The tone delta lambdas name roles declared further down; they only run once the class is initialized.
#pragma warning disable CS8604

namespace Savepoint.App.Theme.Material;

// The DynamicColors of the Material Design system.
public static class MaterialDynamicColors
{
    public static readonly DynamicColor PrimaryPaletteKeyColor = DynamicColor.FromPalette(
        name: "primary_palette_key_color",
        palette: s => s.PrimaryPalette,
        tone: s => s.PrimaryPalette.KeyColor.Tone);

    public static readonly DynamicColor SecondaryPaletteKeyColor = DynamicColor.FromPalette(
        name: "secondary_palette_key_color",
        palette: s => s.SecondaryPalette,
        tone: s => s.SecondaryPalette.KeyColor.Tone);

    public static readonly DynamicColor TertiaryPaletteKeyColor = DynamicColor.FromPalette(
        name: "tertiary_palette_key_color",
        palette: s => s.TertiaryPalette,
        tone: s => s.TertiaryPalette.KeyColor.Tone);

    public static readonly DynamicColor NeutralPaletteKeyColor = DynamicColor.FromPalette(
        name: "neutral_palette_key_color",
        palette: s => s.NeutralPalette,
        tone: s => s.NeutralPalette.KeyColor.Tone);

    public static readonly DynamicColor NeutralVariantPaletteKeyColor = DynamicColor.FromPalette(
        name: "neutral_variant_palette_key_color",
        palette: s => s.NeutralVariantPalette,
        tone: s => s.NeutralVariantPalette.KeyColor.Tone);

    public static readonly DynamicColor Background = DynamicColor.FromPalette(
        name: "background",
        palette: s => s.NeutralPalette,
        tone: s => s.IsDark ? 6 : 98,
        isBackground: true);

    public static readonly DynamicColor OnBackground = DynamicColor.FromPalette(
        name: "on_background",
        palette: s => s.NeutralPalette,
        tone: s => s.IsDark ? 90 : 10,
        background: s => Background,
        contrastCurve: new ContrastCurve(3, 3, 4.5, 7));

    public static readonly DynamicColor Surface = DynamicColor.FromPalette(
        name: "surface",
        palette: s => s.NeutralPalette,
        tone: s => s.IsDark ? 6 : 98,
        isBackground: true);

    public static readonly DynamicColor SurfaceDim = DynamicColor.FromPalette(
        name: "surface_dim",
        palette: s => s.NeutralPalette,
        tone: s => s.IsDark ? 6 : new ContrastCurve(87, 87, 80, 75).Get(s.ContrastLevel),
        isBackground: true);

    public static readonly DynamicColor SurfaceBright = DynamicColor.FromPalette(
        name: "surface_bright",
        palette: s => s.NeutralPalette,
        tone: s => s.IsDark ? new ContrastCurve(24, 24, 29, 34).Get(s.ContrastLevel) : 98,
        isBackground: true);

    public static readonly DynamicColor SurfaceContainerLowest = DynamicColor.FromPalette(
        name: "surface_container_lowest",
        palette: s => s.NeutralPalette,
        tone: s => s.IsDark ? new ContrastCurve(4, 4, 2, 0).Get(s.ContrastLevel) : 100,
        isBackground: true);

    public static readonly DynamicColor SurfaceContainerLow = DynamicColor.FromPalette(
        name: "surface_container_low",
        palette: s => s.NeutralPalette,
        tone: s => s.IsDark
            ? new ContrastCurve(10, 10, 11, 12).Get(s.ContrastLevel)
            : new ContrastCurve(96, 96, 96, 95).Get(s.ContrastLevel),
        isBackground: true);

    public static readonly DynamicColor SurfaceContainer = DynamicColor.FromPalette(
        name: "surface_container",
        palette: s => s.NeutralPalette,
        tone: s => s.IsDark
            ? new ContrastCurve(12, 12, 16, 20).Get(s.ContrastLevel)
            : new ContrastCurve(94, 94, 92, 90).Get(s.ContrastLevel),
        isBackground: true);

    public static readonly DynamicColor SurfaceContainerHigh = DynamicColor.FromPalette(
        name: "surface_container_high",
        palette: s => s.NeutralPalette,
        tone: s => s.IsDark
            ? new ContrastCurve(17, 17, 21, 25).Get(s.ContrastLevel)
            : new ContrastCurve(92, 92, 88, 85).Get(s.ContrastLevel),
        isBackground: true);

    public static readonly DynamicColor SurfaceContainerHighest = DynamicColor.FromPalette(
        name: "surface_container_highest",
        palette: s => s.NeutralPalette,
        tone: s => s.IsDark
            ? new ContrastCurve(22, 22, 26, 30).Get(s.ContrastLevel)
            : new ContrastCurve(90, 90, 84, 80).Get(s.ContrastLevel),
        isBackground: true);

    public static readonly DynamicColor OnSurface = DynamicColor.FromPalette(
        name: "on_surface",
        palette: s => s.NeutralPalette,
        tone: s => s.IsDark ? 90 : 10,
        background: s => HighestSurface(s),
        contrastCurve: new ContrastCurve(4.5, 7, 11, 21));

    public static readonly DynamicColor SurfaceVariant = DynamicColor.FromPalette(
        name: "surface_variant",
        palette: s => s.NeutralVariantPalette,
        tone: s => s.IsDark ? 30 : 90,
        isBackground: true);

    public static readonly DynamicColor OnSurfaceVariant = DynamicColor.FromPalette(
        name: "on_surface_variant",
        palette: s => s.NeutralVariantPalette,
        tone: s => s.IsDark ? 80 : 30,
        background: s => HighestSurface(s),
        contrastCurve: new ContrastCurve(3, 4.5, 7, 11));

    public static readonly DynamicColor InverseSurface = DynamicColor.FromPalette(
        name: "inverse_surface",
        palette: s => s.NeutralPalette,
        tone: s => s.IsDark ? 90 : 20);

    public static readonly DynamicColor InverseOnSurface = DynamicColor.FromPalette(
        name: "inverse_on_surface",
        palette: s => s.NeutralPalette,
        tone: s => s.IsDark ? 20 : 95,
        background: s => InverseSurface,
        contrastCurve: new ContrastCurve(4.5, 7, 11, 21));

    public static readonly DynamicColor Outline = DynamicColor.FromPalette(
        name: "outline",
        palette: s => s.NeutralVariantPalette,
        tone: s => s.IsDark ? 60 : 50,
        background: s => HighestSurface(s),
        contrastCurve: new ContrastCurve(1.5, 3, 4.5, 7));

    public static readonly DynamicColor OutlineVariant = DynamicColor.FromPalette(
        name: "outline_variant",
        palette: s => s.NeutralVariantPalette,
        tone: s => s.IsDark ? 30 : 80,
        background: s => HighestSurface(s),
        contrastCurve: new ContrastCurve(1, 1, 3, 4.5));

    public static readonly DynamicColor Shadow = DynamicColor.FromPalette(
        name: "shadow",
        palette: s => s.NeutralPalette,
        tone: s => 0);

    public static readonly DynamicColor Scrim = DynamicColor.FromPalette(
        name: "scrim",
        palette: s => s.NeutralPalette,
        tone: s => 0);

    public static readonly DynamicColor SurfaceTint = DynamicColor.FromPalette(
        name: "surface_tint",
        palette: s => s.PrimaryPalette,
        tone: s => s.IsDark ? 80 : 40,
        isBackground: true);

    public static readonly DynamicColor Primary = DynamicColor.FromPalette(
        name: "primary",
        palette: s => s.PrimaryPalette,
        tone: s =>
        {
            if (IsMonochrome(s))
            {
                return s.IsDark ? 100 : 0;
            }

            return s.IsDark ? 80 : 40;
        },
        isBackground: true,
        background: s => HighestSurface(s),
        contrastCurve: new ContrastCurve(3, 4.5, 7, 7),
        toneDeltaPair: s => new ToneDeltaPair(PrimaryContainer, Primary, 10, TonePolarity.Nearer, false));

    public static readonly DynamicColor OnPrimary = DynamicColor.FromPalette(
        name: "on_primary",
        palette: s => s.PrimaryPalette,
        tone: s =>
        {
            if (IsMonochrome(s))
            {
                return s.IsDark ? 10 : 90;
            }

            return s.IsDark ? 20 : 100;
        },
        background: s => Primary,
        contrastCurve: new ContrastCurve(4.5, 7, 11, 21));

    public static readonly DynamicColor PrimaryContainer = DynamicColor.FromPalette(
        name: "primary_container",
        palette: s => s.PrimaryPalette,
        tone: s =>
        {
            if (IsFidelity(s))
            {
                return s.SourceColorHct.Tone;
            }

            if (IsMonochrome(s))
            {
                return s.IsDark ? 85 : 25;
            }

            return s.IsDark ? 30 : 90;
        },
        isBackground: true,
        background: s => HighestSurface(s),
        contrastCurve: new ContrastCurve(1, 1, 3, 4.5),
        toneDeltaPair: s => new ToneDeltaPair(PrimaryContainer, Primary, 10, TonePolarity.Nearer, false));

    public static readonly DynamicColor OnPrimaryContainer = DynamicColor.FromPalette(
        name: "on_primary_container",
        palette: s => s.PrimaryPalette,
        tone: s =>
        {
            if (IsFidelity(s))
            {
                return DynamicColor.ForegroundTone(PrimaryContainer.Tone(s), 4.5);
            }

            if (IsMonochrome(s))
            {
                return s.IsDark ? 0 : 100;
            }

            return s.IsDark ? 90 : 30;
        },
        background: s => PrimaryContainer,
        contrastCurve: new ContrastCurve(3, 4.5, 7, 11));

    public static readonly DynamicColor InversePrimary = DynamicColor.FromPalette(
        name: "inverse_primary",
        palette: s => s.PrimaryPalette,
        tone: s => s.IsDark ? 40 : 80,
        background: s => InverseSurface,
        contrastCurve: new ContrastCurve(3, 4.5, 7, 7));

    public static readonly DynamicColor Secondary = DynamicColor.FromPalette(
        name: "secondary",
        palette: s => s.SecondaryPalette,
        tone: s => s.IsDark ? 80 : 40,
        isBackground: true,
        background: s => HighestSurface(s),
        contrastCurve: new ContrastCurve(3, 4.5, 7, 7),
        toneDeltaPair: s => new ToneDeltaPair(SecondaryContainer, Secondary, 10, TonePolarity.Nearer, false));

    public static readonly DynamicColor OnSecondary = DynamicColor.FromPalette(
        name: "on_secondary",
        palette: s => s.SecondaryPalette,
        tone: s =>
        {
            if (IsMonochrome(s))
            {
                return s.IsDark ? 10 : 100;
            }

            return s.IsDark ? 20 : 100;
        },
        background: s => Secondary,
        contrastCurve: new ContrastCurve(4.5, 7, 11, 21));

    public static readonly DynamicColor SecondaryContainer = DynamicColor.FromPalette(
        name: "secondary_container",
        palette: s => s.SecondaryPalette,
        tone: s =>
        {
            var initialTone = s.IsDark ? 30 : 90;
            if (IsMonochrome(s))
            {
                return s.IsDark ? 30 : 85;
            }

            if (!IsFidelity(s))
            {
                return initialTone;
            }

            return FindDesiredChromaByTone(
                s.SecondaryPalette.Hue, s.SecondaryPalette.Chroma, initialTone, !s.IsDark);
        },
        isBackground: true,
        background: s => HighestSurface(s),
        contrastCurve: new ContrastCurve(1, 1, 3, 4.5),
        toneDeltaPair: s => new ToneDeltaPair(SecondaryContainer, Secondary, 10, TonePolarity.Nearer, false));

    public static readonly DynamicColor OnSecondaryContainer = DynamicColor.FromPalette(
        name: "on_secondary_container",
        palette: s => s.SecondaryPalette,
        tone: s =>
        {
            if (IsMonochrome(s))
            {
                return s.IsDark ? 90 : 10;
            }

            if (!IsFidelity(s))
            {
                return s.IsDark ? 90 : 30;
            }

            return DynamicColor.ForegroundTone(SecondaryContainer.Tone(s), 4.5);
        },
        background: s => SecondaryContainer,
        contrastCurve: new ContrastCurve(3, 4.5, 7, 11));

    public static readonly DynamicColor Tertiary = DynamicColor.FromPalette(
        name: "tertiary",
        palette: s => s.TertiaryPalette,
        tone: s =>
        {
            if (IsMonochrome(s))
            {
                return s.IsDark ? 90 : 25;
            }

            return s.IsDark ? 80 : 40;
        },
        isBackground: true,
        background: s => HighestSurface(s),
        contrastCurve: new ContrastCurve(3, 4.5, 7, 7),
        toneDeltaPair: s => new ToneDeltaPair(TertiaryContainer, Tertiary, 10, TonePolarity.Nearer, false));

    public static readonly DynamicColor OnTertiary = DynamicColor.FromPalette(
        name: "on_tertiary",
        palette: s => s.TertiaryPalette,
        tone: s =>
        {
            if (IsMonochrome(s))
            {
                return s.IsDark ? 10 : 90;
            }

            return s.IsDark ? 20 : 100;
        },
        background: s => Tertiary,
        contrastCurve: new ContrastCurve(4.5, 7, 11, 21));

    public static readonly DynamicColor TertiaryContainer = DynamicColor.FromPalette(
        name: "tertiary_container",
        palette: s => s.TertiaryPalette,
        tone: s =>
        {
            if (IsMonochrome(s))
            {
                return s.IsDark ? 60 : 49;
            }

            if (!IsFidelity(s))
            {
                return s.IsDark ? 30 : 90;
            }

            var proposedHct = s.TertiaryPalette.GetHct(s.SourceColorHct.Tone);
            return DislikeAnalyzer.FixIfDisliked(proposedHct).Tone;
        },
        isBackground: true,
        background: s => HighestSurface(s),
        contrastCurve: new ContrastCurve(1, 1, 3, 4.5),
        toneDeltaPair: s => new ToneDeltaPair(TertiaryContainer, Tertiary, 10, TonePolarity.Nearer, false));

    public static readonly DynamicColor OnTertiaryContainer = DynamicColor.FromPalette(
        name: "on_tertiary_container",
        palette: s => s.TertiaryPalette,
        tone: s =>
        {
            if (IsMonochrome(s))
            {
                return s.IsDark ? 0 : 100;
            }

            if (!IsFidelity(s))
            {
                return s.IsDark ? 90 : 30;
            }

            return DynamicColor.ForegroundTone(TertiaryContainer.Tone(s), 4.5);
        },
        background: s => TertiaryContainer,
        contrastCurve: new ContrastCurve(3, 4.5, 7, 11));

    public static readonly DynamicColor Error = DynamicColor.FromPalette(
        name: "error",
        palette: s => s.ErrorPalette,
        tone: s => s.IsDark ? 80 : 40,
        isBackground: true,
        background: s => HighestSurface(s),
        contrastCurve: new ContrastCurve(3, 4.5, 7, 7),
        toneDeltaPair: s => new ToneDeltaPair(ErrorContainer, Error, 10, TonePolarity.Nearer, false));

    public static readonly DynamicColor OnError = DynamicColor.FromPalette(
        name: "on_error",
        palette: s => s.ErrorPalette,
        tone: s => s.IsDark ? 20 : 100,
        background: s => Error,
        contrastCurve: new ContrastCurve(4.5, 7, 11, 21));

    public static readonly DynamicColor ErrorContainer = DynamicColor.FromPalette(
        name: "error_container",
        palette: s => s.ErrorPalette,
        tone: s => s.IsDark ? 30 : 90,
        isBackground: true,
        background: s => HighestSurface(s),
        contrastCurve: new ContrastCurve(1, 1, 3, 4.5),
        toneDeltaPair: s => new ToneDeltaPair(ErrorContainer, Error, 10, TonePolarity.Nearer, false));

    public static readonly DynamicColor OnErrorContainer = DynamicColor.FromPalette(
        name: "on_error_container",
        palette: s => s.ErrorPalette,
        tone: s =>
        {
            if (IsMonochrome(s))
            {
                return s.IsDark ? 90 : 10;
            }

            return s.IsDark ? 90 : 30;
        },
        background: s => ErrorContainer,
        contrastCurve: new ContrastCurve(3, 4.5, 7, 11));

    public static readonly DynamicColor PrimaryFixed = DynamicColor.FromPalette(
        name: "primary_fixed",
        palette: s => s.PrimaryPalette,
        tone: s => IsMonochrome(s) ? 40.0 : 90.0,
        isBackground: true,
        background: s => HighestSurface(s),
        contrastCurve: new ContrastCurve(1, 1, 3, 4.5),
        toneDeltaPair: s => new ToneDeltaPair(PrimaryFixed, PrimaryFixedDim, 10, TonePolarity.Lighter, true));

    public static readonly DynamicColor PrimaryFixedDim = DynamicColor.FromPalette(
        name: "primary_fixed_dim",
        palette: s => s.PrimaryPalette,
        tone: s => IsMonochrome(s) ? 30.0 : 80.0,
        isBackground: true,
        background: s => HighestSurface(s),
        contrastCurve: new ContrastCurve(1, 1, 3, 4.5),
        toneDeltaPair: s => new ToneDeltaPair(PrimaryFixed, PrimaryFixedDim, 10, TonePolarity.Lighter, true));

    public static readonly DynamicColor OnPrimaryFixed = DynamicColor.FromPalette(
        name: "on_primary_fixed",
        palette: s => s.PrimaryPalette,
        tone: s => IsMonochrome(s) ? 100.0 : 10.0,
        background: s => PrimaryFixedDim,
        secondBackground: s => PrimaryFixed,
        contrastCurve: new ContrastCurve(4.5, 7, 11, 21));

    public static readonly DynamicColor OnPrimaryFixedVariant = DynamicColor.FromPalette(
        name: "on_primary_fixed_variant",
        palette: s => s.PrimaryPalette,
        tone: s => IsMonochrome(s) ? 90.0 : 30.0,
        background: s => PrimaryFixedDim,
        secondBackground: s => PrimaryFixed,
        contrastCurve: new ContrastCurve(3, 4.5, 7, 11));

    public static readonly DynamicColor SecondaryFixed = DynamicColor.FromPalette(
        name: "secondary_fixed",
        palette: s => s.SecondaryPalette,
        tone: s => IsMonochrome(s) ? 80.0 : 90.0,
        isBackground: true,
        background: s => HighestSurface(s),
        contrastCurve: new ContrastCurve(1, 1, 3, 4.5),
        toneDeltaPair: s => new ToneDeltaPair(SecondaryFixed, SecondaryFixedDim, 10, TonePolarity.Lighter, true));

    public static readonly DynamicColor SecondaryFixedDim = DynamicColor.FromPalette(
        name: "secondary_fixed_dim",
        palette: s => s.SecondaryPalette,
        tone: s => IsMonochrome(s) ? 70.0 : 80.0,
        isBackground: true,
        background: s => HighestSurface(s),
        contrastCurve: new ContrastCurve(1, 1, 3, 4.5),
        toneDeltaPair: s => new ToneDeltaPair(SecondaryFixed, SecondaryFixedDim, 10, TonePolarity.Lighter, true));

    public static readonly DynamicColor OnSecondaryFixed = DynamicColor.FromPalette(
        name: "on_secondary_fixed",
        palette: s => s.SecondaryPalette,
        tone: s => 10.0,
        background: s => SecondaryFixedDim,
        secondBackground: s => SecondaryFixed,
        contrastCurve: new ContrastCurve(4.5, 7, 11, 21));

    public static readonly DynamicColor OnSecondaryFixedVariant = DynamicColor.FromPalette(
        name: "on_secondary_fixed_variant",
        palette: s => s.SecondaryPalette,
        tone: s => IsMonochrome(s) ? 25.0 : 30.0,
        background: s => SecondaryFixedDim,
        secondBackground: s => SecondaryFixed,
        contrastCurve: new ContrastCurve(3, 4.5, 7, 11));

    public static readonly DynamicColor TertiaryFixed = DynamicColor.FromPalette(
        name: "tertiary_fixed",
        palette: s => s.TertiaryPalette,
        tone: s => IsMonochrome(s) ? 40.0 : 90.0,
        isBackground: true,
        background: s => HighestSurface(s),
        contrastCurve: new ContrastCurve(1, 1, 3, 4.5),
        toneDeltaPair: s => new ToneDeltaPair(TertiaryFixed, TertiaryFixedDim, 10, TonePolarity.Lighter, true));

    public static readonly DynamicColor TertiaryFixedDim = DynamicColor.FromPalette(
        name: "tertiary_fixed_dim",
        palette: s => s.TertiaryPalette,
        tone: s => IsMonochrome(s) ? 30.0 : 80.0,
        isBackground: true,
        background: s => HighestSurface(s),
        contrastCurve: new ContrastCurve(1, 1, 3, 4.5),
        toneDeltaPair: s => new ToneDeltaPair(TertiaryFixed, TertiaryFixedDim, 10, TonePolarity.Lighter, true));

    public static readonly DynamicColor OnTertiaryFixed = DynamicColor.FromPalette(
        name: "on_tertiary_fixed",
        palette: s => s.TertiaryPalette,
        tone: s => IsMonochrome(s) ? 100.0 : 10.0,
        background: s => TertiaryFixedDim,
        secondBackground: s => TertiaryFixed,
        contrastCurve: new ContrastCurve(4.5, 7, 11, 21));

    public static readonly DynamicColor OnTertiaryFixedVariant = DynamicColor.FromPalette(
        name: "on_tertiary_fixed_variant",
        palette: s => s.TertiaryPalette,
        tone: s => IsMonochrome(s) ? 90.0 : 30.0,
        background: s => TertiaryFixedDim,
        secondBackground: s => TertiaryFixed,
        contrastCurve: new ContrastCurve(3, 4.5, 7, 11));

    public static DynamicColor HighestSurface(DynamicScheme scheme) => scheme.IsDark ? SurfaceBright : SurfaceDim;

    private static bool IsFidelity(DynamicScheme scheme) =>
        scheme.Variant == Variant.Fidelity || scheme.Variant == Variant.Content;

    private static bool IsMonochrome(DynamicScheme scheme) => scheme.Variant == Variant.Monochrome;

    private static double FindDesiredChromaByTone(double hue, double chroma, double tone, bool byDecreasingTone)
    {
        var answer = tone;

        var closestToChroma = Hct.From(hue, chroma, tone);
        if (closestToChroma.Chroma < chroma)
        {
            var chromaPeak = closestToChroma.Chroma;
            while (closestToChroma.Chroma < chroma)
            {
                answer += byDecreasingTone ? -1.0 : 1.0;
                var potentialSolution = Hct.From(hue, chroma, answer);
                if (chromaPeak > potentialSolution.Chroma)
                {
                    break;
                }

                if (Math.Abs(potentialSolution.Chroma - chroma) < 0.4)
                {
                    break;
                }

                var potentialDelta = Math.Abs(potentialSolution.Chroma - chroma);
                var currentDelta = Math.Abs(closestToChroma.Chroma - chroma);
                if (potentialDelta < currentDelta)
                {
                    closestToChroma = potentialSolution;
                }

                chromaPeak = Math.Max(chromaPeak, potentialSolution.Chroma);
            }
        }

        return answer;
    }
}
