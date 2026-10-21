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
// Modified: translated to C# from utils/math_utils.js in @material/material-color-utilities 0.3.0.

namespace Savepoint.App.Theme.Material;

public static class MathUtils
{
    // JS Math.round rounds halves toward +infinity; Math.Round defaults to banker's rounding.
    public static double JsRound(double value)
    {
        var floor = Math.Floor(value);
        return value - floor >= 0.5 ? floor + 1.0 : floor;
    }

    public static double Signum(double value)
    {
        if (value < 0)
        {
            return -1.0;
        }

        return value == 0 ? 0.0 : 1.0;
    }

    public static double Lerp(double start, double stop, double amount) => (1.0 - amount) * start + amount * stop;

    public static double ClampDouble(double min, double max, double input)
    {
        if (input < min)
        {
            return min;
        }

        return input > max ? max : input;
    }

    public static double SanitizeDegreesDouble(double degrees)
    {
        degrees %= 360.0;
        if (degrees < 0)
        {
            degrees += 360.0;
        }

        return degrees;
    }

    public static double[] MatrixMultiply(double[] row, double[][] matrix)
    {
        var a = row[0] * matrix[0][0] + row[1] * matrix[0][1] + row[2] * matrix[0][2];
        var b = row[0] * matrix[1][0] + row[1] * matrix[1][1] + row[2] * matrix[1][2];
        var c = row[0] * matrix[2][0] + row[1] * matrix[2][1] + row[2] * matrix[2][2];
        return [a, b, c];
    }
}
