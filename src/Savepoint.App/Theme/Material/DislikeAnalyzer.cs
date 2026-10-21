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
// Modified: translated to C# from dislike/dislike_analyzer.js in @material/material-color-utilities 0.3.0.

namespace Savepoint.App.Theme.Material;

// Dark yellow-greens are widely disliked, so colors that land there get lightened.
public static class DislikeAnalyzer
{
    public static bool IsDisliked(Hct hct) => IsDisliked(hct.Hue, hct.Chroma, hct.Tone);

    public static bool IsDisliked(double hue, double chroma, double tone)
    {
        var huePasses = MathUtils.JsRound(hue) >= 90.0 && MathUtils.JsRound(hue) <= 111.0;
        var chromaPasses = MathUtils.JsRound(chroma) > 16.0;
        var tonePasses = MathUtils.JsRound(tone) < 65.0;
        return huePasses && chromaPasses && tonePasses;
    }

    public static Hct FixIfDisliked(Hct hct) => IsDisliked(hct) ? Hct.From(hct.Hue, hct.Chroma, 70.0) : hct;
}
