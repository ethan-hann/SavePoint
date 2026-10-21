// Copyright 2023 Google LLC
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
// Modified: translated to C# from dynamiccolor/contrast_curve.js in @material/material-color-utilities 0.3.0.

namespace Savepoint.App.Theme.Material;

// A value that changes with the contrast level: the four anchors are the values at levels -1.0, 0.0, 0.5 and 1.0.
public sealed class ContrastCurve(double low, double normal, double medium, double high)
{
    public double Low { get; } = low;

    public double Normal { get; } = normal;

    public double Medium { get; } = medium;

    public double High { get; } = high;

    public double Get(double contrastLevel)
    {
        if (contrastLevel <= -1.0)
        {
            return Low;
        }

        if (contrastLevel < 0.0)
        {
            return MathUtils.Lerp(Low, Normal, (contrastLevel - -1.0) / 1.0);
        }

        if (contrastLevel < 0.5)
        {
            return MathUtils.Lerp(Normal, Medium, (contrastLevel - 0.0) / 0.5);
        }

        if (contrastLevel < 1.0)
        {
            return MathUtils.Lerp(Medium, High, (contrastLevel - 0.5) / 0.5);
        }

        return High;
    }
}
