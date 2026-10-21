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
// Modified: translated to C# from palettes/tonal_palette.js in @material/material-color-utilities 0.3.0.

using System.Collections.Concurrent;

namespace Savepoint.App.Theme.Material;

public sealed class TonalPalette
{
    // Concurrent because one palette can serve role lookups from several threads.
    private readonly ConcurrentDictionary<double, uint> _cache = new();

    private TonalPalette(double hue, double chroma, Hct keyColor)
    {
        Hue = hue;
        Chroma = chroma;
        KeyColor = keyColor;
    }

    public double Hue { get; }

    public double Chroma { get; }

    public Hct KeyColor { get; }

    public static TonalPalette FromInt(uint argb) => FromHct(Hct.FromInt(argb));

    public static TonalPalette FromHct(Hct hct) => new(hct.Hue, hct.Chroma, hct);

    public static TonalPalette FromHueAndChroma(double hue, double chroma) =>
        new(hue, chroma, new KeyColorSearch(hue, chroma).Create());

    public uint Tone(double tone) =>
        _cache.GetOrAdd(tone, static (key, palette) => Hct.From(palette.Hue, palette.Chroma, key).ToInt(), this);

    public Hct GetHct(double tone) => Hct.FromInt(Tone(tone));

    private sealed class KeyColorSearch(double hue, double requestedChroma)
    {
        private readonly Dictionary<int, double> _chromaCache = [];

        public Hct Create()
        {
            // T50 has the most chroma available on average, so a direct answer is most likely there.
            const int pivotTone = 50;
            const int toneStepSize = 1;
            const double epsilon = 0.01;

            var lowerTone = 0;
            var upperTone = 100;
            while (lowerTone < upperTone)
            {
                var midTone = (lowerTone + upperTone) / 2;
                var isAscending = MaxChroma(midTone) < MaxChroma(midTone + toneStepSize);
                var sufficientChroma = MaxChroma(midTone) >= requestedChroma - epsilon;
                if (sufficientChroma)
                {
                    // Either [lowerTone, midTone] or [midTone, upperTone] has the answer; take the half nearer the pivot.
                    if (Math.Abs(lowerTone - pivotTone) < Math.Abs(upperTone - pivotTone))
                    {
                        upperTone = midTone;
                    }
                    else
                    {
                        if (lowerTone == midTone)
                        {
                            return Hct.From(hue, requestedChroma, lowerTone);
                        }

                        lowerTone = midTone;
                    }
                }
                else if (isAscending)
                {
                    lowerTone = midTone + toneStepSize;
                }
                else
                {
                    // Keep midTone: it may be the chroma peak.
                    upperTone = midTone;
                }
            }

            return Hct.From(hue, requestedChroma, lowerTone);
        }

        private double MaxChroma(int tone)
        {
            if (_chromaCache.TryGetValue(tone, out var cached))
            {
                return cached;
            }

            var chroma = Hct.From(hue, 200.0, tone).Chroma;
            _chromaCache[tone] = chroma;
            return chroma;
        }
    }
}
