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
// Modified: translated to C# from contrast/contrast.js in @material/material-color-utilities 0.3.0.

namespace Savepoint.App.Theme.Material;

// Contrast ratios between tones. Tone is HCT tone, which equals L* in L*a*b*.
public static class Contrast
{
    // A ratio from 1 to 21; tones outside 0..100 are clamped.
    public static double RatioOfTones(double toneA, double toneB)
    {
        toneA = MathUtils.ClampDouble(0.0, 100.0, toneA);
        toneB = MathUtils.ClampDouble(0.0, 100.0, toneB);
        return RatioOfYs(ColorUtils.YFromLstar(toneA), ColorUtils.YFromLstar(toneB));
    }

    public static double RatioOfYs(double y1, double y2)
    {
        var lighter = y1 > y2 ? y1 : y2;
        var darker = lighter == y2 ? y1 : y2;
        return (lighter + 5.0) / (darker + 5.0);
    }

    // A tone at least as light as the given one that reaches the ratio, or -1 if none does.
    public static double Lighter(double tone, double ratio)
    {
        if (tone < 0.0 || tone > 100.0)
        {
            return -1.0;
        }

        var darkY = ColorUtils.YFromLstar(tone);
        var lightY = ratio * (darkY + 5.0) - 5.0;
        var realContrast = RatioOfYs(lightY, darkY);
        var delta = Math.Abs(realContrast - ratio);
        if (realContrast < ratio && delta > 0.04)
        {
            return -1;
        }

        // Gamut mapping works on a tone range, so nudge by 0.4 to keep the ratio after mapping.
        var returnValue = ColorUtils.LstarFromY(lightY) + 0.4;
        if (returnValue < 0 || returnValue > 100)
        {
            return -1;
        }

        return returnValue;
    }

    // A tone at least as dark as the given one that reaches the ratio, or -1 if none does.
    public static double Darker(double tone, double ratio)
    {
        if (tone < 0.0 || tone > 100.0)
        {
            return -1.0;
        }

        var lightY = ColorUtils.YFromLstar(tone);
        var darkY = (lightY + 5.0) / ratio - 5.0;
        var realContrast = RatioOfYs(lightY, darkY);
        var delta = Math.Abs(realContrast - ratio);
        if (realContrast < ratio && delta > 0.04)
        {
            return -1;
        }

        var returnValue = ColorUtils.LstarFromY(darkY) - 0.4;
        if (returnValue < 0 || returnValue > 100)
        {
            return -1;
        }

        return returnValue;
    }

    // Like Lighter, but returns tone 100 when the ratio cannot be reached.
    public static double LighterUnsafe(double tone, double ratio)
    {
        var lighterSafe = Lighter(tone, ratio);
        return lighterSafe < 0.0 ? 100.0 : lighterSafe;
    }

    // Like Darker, but returns tone 0 when the ratio cannot be reached.
    public static double DarkerUnsafe(double tone, double ratio)
    {
        var darkerSafe = Darker(tone, ratio);
        return darkerSafe < 0.0 ? 0.0 : darkerSafe;
    }
}
