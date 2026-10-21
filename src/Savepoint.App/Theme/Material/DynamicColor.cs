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
// Modified: translated to C# from dynamiccolor/dynamic_color.js in @material/material-color-utilities 0.3.0, without its HCT cache.

namespace Savepoint.App.Theme.Material;

public sealed class DynamicColor
{
    private DynamicColor(
        string name,
        Func<DynamicScheme, TonalPalette> palette,
        Func<DynamicScheme, double> tone,
        bool isBackground,
        Func<DynamicScheme, DynamicColor>? background,
        Func<DynamicScheme, DynamicColor>? secondBackground,
        ContrastCurve? contrastCurve,
        Func<DynamicScheme, ToneDeltaPair>? toneDeltaPair)
    {
        Name = name;
        Palette = palette;
        Tone = tone;
        IsBackground = isBackground;
        Background = background;
        SecondBackground = secondBackground;
        ContrastCurve = contrastCurve;
        ToneDeltaPair = toneDeltaPair;

        if (background is null && secondBackground is not null)
        {
            throw new ArgumentException($"Color {name} has secondBackground defined, but background is not defined.");
        }

        if (background is null && contrastCurve is not null)
        {
            throw new ArgumentException($"Color {name} has contrastCurve defined, but background is not defined.");
        }

        if (background is not null && contrastCurve is null)
        {
            throw new ArgumentException($"Color {name} has background defined, but contrastCurve is not defined.");
        }
    }

    public string Name { get; }

    public Func<DynamicScheme, TonalPalette> Palette { get; }

    public Func<DynamicScheme, double> Tone { get; }

    public bool IsBackground { get; }

    public Func<DynamicScheme, DynamicColor>? Background { get; }

    public Func<DynamicScheme, DynamicColor>? SecondBackground { get; }

    public ContrastCurve? ContrastCurve { get; }

    public Func<DynamicScheme, ToneDeltaPair>? ToneDeltaPair { get; }

    public static DynamicColor FromPalette(
        string name,
        Func<DynamicScheme, TonalPalette> palette,
        Func<DynamicScheme, double> tone,
        bool isBackground = false,
        Func<DynamicScheme, DynamicColor>? background = null,
        Func<DynamicScheme, DynamicColor>? secondBackground = null,
        ContrastCurve? contrastCurve = null,
        Func<DynamicScheme, ToneDeltaPair>? toneDeltaPair = null) =>
        new(name, palette, tone, isBackground, background, secondBackground, contrastCurve, toneDeltaPair);

    public static double ForegroundTone(double bgTone, double ratio)
    {
        var lighterTone = Contrast.LighterUnsafe(bgTone, ratio);
        var darkerTone = Contrast.DarkerUnsafe(bgTone, ratio);
        var lighterRatio = Contrast.RatioOfTones(lighterTone, bgTone);
        var darkerRatio = Contrast.RatioOfTones(darkerTone, bgTone);
        var preferLighter = TonePrefersLightForeground(bgTone);

        if (preferLighter)
        {
            // If both options fall short and nearly tie, keep the lighter so on-primary-container does not flip to black.
            var negligibleDifference = Math.Abs(lighterRatio - darkerRatio) < 0.1 && lighterRatio < ratio && darkerRatio < ratio;
            return lighterRatio >= ratio || lighterRatio >= darkerRatio || negligibleDifference ? lighterTone : darkerTone;
        }

        return darkerRatio >= ratio || darkerRatio >= lighterRatio ? darkerTone : lighterTone;
    }

    // T60 is excluded so tertiary container in the dark monochrome scheme is left alone.
    public static bool TonePrefersLightForeground(double tone) => MathUtils.JsRound(tone) < 60.0;

    public static bool ToneAllowsLightForeground(double tone) => MathUtils.JsRound(tone) <= 49.0;

    public static double EnableLightForeground(double tone) =>
        TonePrefersLightForeground(tone) && !ToneAllowsLightForeground(tone) ? 49.0 : tone;

    public uint GetArgb(DynamicScheme scheme) => Palette(scheme).Tone(GetTone(scheme));

    public Hct GetHct(DynamicScheme scheme) => Palette(scheme).GetHct(GetTone(scheme));

    public double GetTone(DynamicScheme scheme)
    {
        var decreasingContrast = scheme.ContrastLevel < 0;

        if (ToneDeltaPair is not null)
        {
            return GetToneForPair(scheme, ToneDeltaPair(scheme), decreasingContrast);
        }

        var answer = Tone(scheme);
        if (Background is null)
        {
            return answer;
        }

        var bgTone = Background(scheme).GetTone(scheme);
        var desiredRatio = ContrastCurve!.Get(scheme.ContrastLevel);

        if (Contrast.RatioOfTones(bgTone, answer) < desiredRatio)
        {
            answer = ForegroundTone(bgTone, desiredRatio);
        }

        if (decreasingContrast)
        {
            answer = ForegroundTone(bgTone, desiredRatio);
        }

        if (IsBackground && answer >= 50 && answer < 60)
        {
            answer = Contrast.RatioOfTones(49, bgTone) >= desiredRatio ? 49 : 60;
        }

        if (SecondBackground is null)
        {
            return answer;
        }

        var secondBgTone = SecondBackground(scheme).GetTone(scheme);
        var upper = Math.Max(bgTone, secondBgTone);
        var lower = Math.Min(bgTone, secondBgTone);

        if (Contrast.RatioOfTones(upper, answer) >= desiredRatio && Contrast.RatioOfTones(lower, answer) >= desiredRatio)
        {
            return answer;
        }

        var lightOption = Contrast.Lighter(upper, desiredRatio);
        var darkOption = Contrast.Darker(lower, desiredRatio);

        var availableCount = (lightOption != -1 ? 1 : 0) + (darkOption != -1 ? 1 : 0);
        var prefersLight = TonePrefersLightForeground(bgTone) || TonePrefersLightForeground(secondBgTone);
        if (prefersLight)
        {
            return lightOption < 0 ? 100 : lightOption;
        }

        if (availableCount == 1)
        {
            return lightOption != -1 ? lightOption : darkOption;
        }

        return darkOption < 0 ? 0 : darkOption;
    }

    private double GetToneForPair(DynamicScheme scheme, ToneDeltaPair pair, bool decreasingContrast)
    {
        var roleA = pair.RoleA;
        var roleB = pair.RoleB;
        var delta = pair.Delta;
        var polarity = pair.Polarity;
        var stayTogether = pair.StayTogether;

        var bgTone = Background!(scheme).GetTone(scheme);

        var aIsNearer = polarity == TonePolarity.Nearer
            || (polarity == TonePolarity.Lighter && !scheme.IsDark)
            || (polarity == TonePolarity.Darker && scheme.IsDark);
        var nearer = aIsNearer ? roleA : roleB;
        var farther = aIsNearer ? roleB : roleA;
        var amNearer = Name == nearer.Name;
        var expansionDir = scheme.IsDark ? 1 : -1;

        var nContrast = nearer.ContrastCurve!.Get(scheme.ContrastLevel);
        var fContrast = farther.ContrastCurve!.Get(scheme.ContrastLevel);

        var nInitialTone = nearer.Tone(scheme);
        var nTone = Contrast.RatioOfTones(bgTone, nInitialTone) >= nContrast
            ? nInitialTone
            : ForegroundTone(bgTone, nContrast);

        var fInitialTone = farther.Tone(scheme);
        var fTone = Contrast.RatioOfTones(bgTone, fInitialTone) >= fContrast
            ? fInitialTone
            : ForegroundTone(bgTone, fContrast);

        if (decreasingContrast)
        {
            // Lowering contrast settles on the bare minimum that satisfies the ratio.
            nTone = ForegroundTone(bgTone, nContrast);
            fTone = ForegroundTone(bgTone, fContrast);
        }

        if ((fTone - nTone) * expansionDir < delta)
        {
            fTone = MathUtils.ClampDouble(0, 100, nTone + delta * expansionDir);
            if ((fTone - nTone) * expansionDir < delta)
            {
                nTone = MathUtils.ClampDouble(0, 100, fTone - delta * expansionDir);
            }
        }

        // Stay out of the 50-59 awkward zone.
        if (nTone >= 50 && nTone < 60)
        {
            if (expansionDir > 0)
            {
                nTone = 60;
                fTone = Math.Max(fTone, nTone + delta * expansionDir);
            }
            else
            {
                nTone = 49;
                fTone = Math.Min(fTone, nTone + delta * expansionDir);
            }
        }
        else if (fTone >= 50 && fTone < 60)
        {
            if (stayTogether)
            {
                // Fix both, so the pair never ends up on opposite sides of the zone.
                if (expansionDir > 0)
                {
                    nTone = 60;
                    fTone = Math.Max(fTone, nTone + delta * expansionDir);
                }
                else
                {
                    nTone = 49;
                    fTone = Math.Min(fTone, nTone + delta * expansionDir);
                }
            }
            else
            {
                fTone = expansionDir > 0 ? 60 : 49;
            }
        }

        return amNearer ? nTone : fTone;
    }
}
