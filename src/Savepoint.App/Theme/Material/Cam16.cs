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
// Modified: translated to C# from hct/cam16.js in @material/material-color-utilities 0.3.0.

namespace Savepoint.App.Theme.Material;

internal sealed class Cam16
{
    private Cam16(double hue, double chroma, double j, double q, double m, double s, double jstar, double astar, double bstar)
    {
        Hue = hue;
        Chroma = chroma;
        J = j;
        Q = q;
        M = m;
        S = s;
        Jstar = jstar;
        Astar = astar;
        Bstar = bstar;
    }

    public double Hue { get; }

    public double Chroma { get; }

    public double J { get; }

    public double Q { get; }

    public double M { get; }

    public double S { get; }

    public double Jstar { get; }

    public double Astar { get; }

    public double Bstar { get; }

    public static Cam16 FromInt(uint argb) => FromIntInViewingConditions(argb, ViewingConditions.Default);

    public static Cam16 FromIntInViewingConditions(uint argb, ViewingConditions viewingConditions)
    {
        var red = (int)((argb & 0x00ff0000) >> 16);
        var green = (int)((argb & 0x0000ff00) >> 8);
        var blue = (int)(argb & 0x000000ff);
        var redL = ColorUtils.Linearized(red);
        var greenL = ColorUtils.Linearized(green);
        var blueL = ColorUtils.Linearized(blue);
        var x = 0.41233895 * redL + 0.35762064 * greenL + 0.18051042 * blueL;
        var y = 0.2126 * redL + 0.7152 * greenL + 0.0722 * blueL;
        var z = 0.01932141 * redL + 0.11916382 * greenL + 0.95034478 * blueL;
        var rC = 0.401288 * x + 0.650173 * y - 0.051461 * z;
        var gC = -0.250268 * x + 1.204414 * y + 0.045854 * z;
        var bC = -0.002079 * x + 0.048952 * y + 0.953127 * z;
        var rD = viewingConditions.RgbD[0] * rC;
        var gD = viewingConditions.RgbD[1] * gC;
        var bD = viewingConditions.RgbD[2] * bC;
        var rAF = Math.Pow(viewingConditions.Fl * Math.Abs(rD) / 100.0, 0.42);
        var gAF = Math.Pow(viewingConditions.Fl * Math.Abs(gD) / 100.0, 0.42);
        var bAF = Math.Pow(viewingConditions.Fl * Math.Abs(bD) / 100.0, 0.42);
        var rA = MathUtils.Signum(rD) * 400.0 * rAF / (rAF + 27.13);
        var gA = MathUtils.Signum(gD) * 400.0 * gAF / (gAF + 27.13);
        var bA = MathUtils.Signum(bD) * 400.0 * bAF / (bAF + 27.13);
        var a = (11.0 * rA + -12.0 * gA + bA) / 11.0;
        var b = (rA + gA - 2.0 * bA) / 9.0;
        var u = (20.0 * rA + 20.0 * gA + 21.0 * bA) / 20.0;
        var p2 = (40.0 * rA + 20.0 * gA + bA) / 20.0;
        var atan2 = Math.Atan2(b, a);
        var atanDegrees = atan2 * 180.0 / Math.PI;
        var hue = atanDegrees < 0
            ? atanDegrees + 360.0
            : atanDegrees >= 360 ? atanDegrees - 360.0 : atanDegrees;
        var hueRadians = hue * Math.PI / 180.0;
        var ac = p2 * viewingConditions.Nbb;
        var j = 100.0 * Math.Pow(ac / viewingConditions.Aw, viewingConditions.C * viewingConditions.Z);
        var q = 4.0 / viewingConditions.C * Math.Sqrt(j / 100.0) * (viewingConditions.Aw + 4.0) * viewingConditions.FLRoot;
        var huePrime = hue < 20.14 ? hue + 360 : hue;
        var eHue = 0.25 * (Math.Cos(huePrime * Math.PI / 180.0 + 2.0) + 3.8);
        var p1 = 50000.0 / 13.0 * eHue * viewingConditions.Nc * viewingConditions.Ncb;
        var t = p1 * Math.Sqrt(a * a + b * b) / (u + 0.305);
        var alpha = Math.Pow(t, 0.9) * Math.Pow(1.64 - Math.Pow(0.29, viewingConditions.N), 0.73);
        var c = alpha * Math.Sqrt(j / 100.0);
        var m = c * viewingConditions.FLRoot;
        var s = 50.0 * Math.Sqrt(alpha * viewingConditions.C / (viewingConditions.Aw + 4.0));
        var jstar = (1.0 + 100.0 * 0.007) * j / (1.0 + 0.007 * j);
        var mstar = 1.0 / 0.0228 * Math.Log(1.0 + 0.0228 * m);
        var astar = mstar * Math.Cos(hueRadians);
        var bstar = mstar * Math.Sin(hueRadians);
        return new Cam16(hue, c, j, q, m, s, jstar, astar, bstar);
    }
}
