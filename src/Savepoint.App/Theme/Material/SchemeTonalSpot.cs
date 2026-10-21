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
// Modified: translated to C# from scheme/scheme_tonal_spot.js in @material/material-color-utilities 0.3.0.

namespace Savepoint.App.Theme.Material;

// Low to medium colorfulness with a tertiary hue related to the source color: the default Material You
// theme on Android 12 and 13.
public sealed class SchemeTonalSpot(Hct sourceColorHct, bool isDark, double contrastLevel)
    : DynamicScheme(new DynamicSchemeArgs(
        sourceColorHct.ToInt(),
        Variant.TonalSpot,
        contrastLevel,
        isDark,
        TonalPalette.FromHueAndChroma(sourceColorHct.Hue, 36.0),
        TonalPalette.FromHueAndChroma(sourceColorHct.Hue, 16.0),
        TonalPalette.FromHueAndChroma(MathUtils.SanitizeDegreesDouble(sourceColorHct.Hue + 60.0), 24.0),
        TonalPalette.FromHueAndChroma(sourceColorHct.Hue, 6.0),
        TonalPalette.FromHueAndChroma(sourceColorHct.Hue, 8.0)));
