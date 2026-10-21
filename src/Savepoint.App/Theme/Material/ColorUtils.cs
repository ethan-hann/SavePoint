// Copyright 2021 Google LLC
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
// Modified: translated to C# from utils/color_utils.js in @material/material-color-utilities 0.3.0.

namespace Savepoint.App.Theme.Material;

public static class ColorUtils
{
    private static readonly double[][] _srgbToXyz =
    [
        [0.41233895, 0.35762064, 0.18051042],
        [0.2126, 0.7152, 0.0722],
        [0.01932141, 0.11916382, 0.95034478],
    ];

    public static uint ArgbFromRgb(int red, int green, int blue) =>
        0xFF000000u | (uint)((red & 255) << 16) | (uint)((green & 255) << 8) | (uint)(blue & 255);

    public static uint ArgbFromLinrgb(double[] linrgb)
    {
        var r = Delinearized(linrgb[0]);
        var g = Delinearized(linrgb[1]);
        var b = Delinearized(linrgb[2]);
        return ArgbFromRgb(r, g, b);
    }

    public static int RedFromArgb(uint argb) => (int)((argb >> 16) & 255);

    public static int GreenFromArgb(uint argb) => (int)((argb >> 8) & 255);

    public static int BlueFromArgb(uint argb) => (int)(argb & 255);

    public static double[] XyzFromArgb(uint argb)
    {
        var r = Linearized(RedFromArgb(argb));
        var g = Linearized(GreenFromArgb(argb));
        var b = Linearized(BlueFromArgb(argb));
        return MathUtils.MatrixMultiply([r, g, b], _srgbToXyz);
    }

    public static uint ArgbFromLstar(double lstar)
    {
        var y = YFromLstar(lstar);
        var component = Delinearized(y);
        return ArgbFromRgb(component, component, component);
    }

    public static double LstarFromArgb(uint argb)
    {
        var y = XyzFromArgb(argb)[1];
        return 116.0 * LabF(y / 100.0) - 16.0;
    }

    public static double YFromLstar(double lstar) => 100.0 * LabInvf((lstar + 16.0) / 116.0);

    public static double LstarFromY(double y) => LabF(y / 100.0) * 116.0 - 16.0;

    public static double Linearized(int rgbComponent)
    {
        var normalized = rgbComponent / 255.0;
        if (normalized <= 0.040449936)
        {
            return normalized / 12.92 * 100.0;
        }

        return Math.Pow((normalized + 0.055) / 1.055, 2.4) * 100.0;
    }

    public static int Delinearized(double rgbComponent)
    {
        var normalized = rgbComponent / 100.0;
        double delinearized;
        if (normalized <= 0.0031308)
        {
            delinearized = normalized * 12.92;
        }
        else
        {
            delinearized = 1.055 * Math.Pow(normalized, 1.0 / 2.4) - 0.055;
        }

        // Clamped as a double first: an out-of-range or NaN value must not wrap through the int cast.
        return (int)MathUtils.ClampDouble(0, 255, MathUtils.JsRound(delinearized * 255.0));
    }

    public static double[] WhitePointD65() => [95.047, 100.0, 108.883];

    private static double LabF(double t)
    {
        var e = 216.0 / 24389.0;
        var kappa = 24389.0 / 27.0;
        if (t > e)
        {
            return Math.Pow(t, 1.0 / 3.0);
        }

        return (kappa * t + 16) / 116;
    }

    private static double LabInvf(double ft)
    {
        var e = 216.0 / 24389.0;
        var kappa = 24389.0 / 27.0;
        var ft3 = ft * ft * ft;
        if (ft3 > e)
        {
            return ft3;
        }

        return (116 * ft - 16) / kappa;
    }
}
