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
// Modified: translated to C# from dynamiccolor/dynamic_scheme.js in @material/material-color-utilities 0.3.0.
// The per-role getters and getRotatedHue are not ported; roles are read through MaterialDynamicColors.

namespace Savepoint.App.Theme.Material;

public sealed record DynamicSchemeArgs(
    uint SourceColorArgb,
    Variant Variant,
    double ContrastLevel,
    bool IsDark,
    TonalPalette PrimaryPalette,
    TonalPalette SecondaryPalette,
    TonalPalette TertiaryPalette,
    TonalPalette NeutralPalette,
    TonalPalette NeutralVariantPalette);

// The UI state (light or dark, theme variant, contrast) and the tonal palettes that DynamicColor resolves against.
public class DynamicScheme
{
    public DynamicScheme(DynamicSchemeArgs args)
    {
        SourceColorArgb = args.SourceColorArgb;
        Variant = args.Variant;
        ContrastLevel = args.ContrastLevel;
        IsDark = args.IsDark;
        SourceColorHct = Hct.FromInt(args.SourceColorArgb);
        PrimaryPalette = args.PrimaryPalette;
        SecondaryPalette = args.SecondaryPalette;
        TertiaryPalette = args.TertiaryPalette;
        NeutralPalette = args.NeutralPalette;
        NeutralVariantPalette = args.NeutralVariantPalette;
        ErrorPalette = TonalPalette.FromHueAndChroma(25.0, 84.0);
    }

    public uint SourceColorArgb { get; }

    public Hct SourceColorHct { get; }

    public Variant Variant { get; }

    // -1 is the lowest contrast, 0 the standard, 1 the highest.
    public double ContrastLevel { get; }

    public bool IsDark { get; }

    public TonalPalette PrimaryPalette { get; }

    public TonalPalette SecondaryPalette { get; }

    public TonalPalette TertiaryPalette { get; }

    public TonalPalette NeutralPalette { get; }

    public TonalPalette NeutralVariantPalette { get; }

    public TonalPalette ErrorPalette { get; }

    public uint GetArgb(DynamicColor dynamicColor) => dynamicColor.GetArgb(this);

    public Hct GetHct(DynamicColor dynamicColor) => dynamicColor.GetHct(this);
}
