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
// Modified: translated to C# from hct/hct.js in @material/material-color-utilities 0.3.0. The mutable
// hue, chroma and tone setters and inViewingConditions are not ported; instances are immutable.

namespace Savepoint.App.Theme.Material;

// Hue and chroma from CAM16, tone from L*a*b*. Tone ties the color system to contrast: a tone difference of
// 40 guarantees a contrast ratio of at least 3.0 and a difference of 50 guarantees at least 4.5.
public sealed class Hct
{
    private readonly uint _argb;

    private Hct(uint argb)
    {
        var cam = Cam16.FromInt(argb);
        Hue = cam.Hue;
        Chroma = cam.Chroma;
        Tone = ColorUtils.LstarFromArgb(argb);
        _argb = argb;
    }

    // Degrees, 0 <= hue < 360.
    public double Hue { get; }

    public double Chroma { get; }

    // L*, 0 to 100.
    public double Tone { get; }

    // Chroma may come back lower than requested: each hue and tone has its own maximum.
    public static Hct From(double hue, double chroma, double tone) => new(HctSolver.SolveToInt(hue, chroma, tone));

    public static Hct FromInt(uint argb) => new(argb);

    public uint ToInt() => _argb;
}
