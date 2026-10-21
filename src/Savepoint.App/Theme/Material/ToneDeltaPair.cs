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
// Modified: translated to C# from dynamiccolor/tone_delta_pair.js in @material/material-color-utilities 0.3.0, with an enum for polarity.

namespace Savepoint.App.Theme.Material;

// Nearer and Farther are relative to the surface roles: nearer means lighter in light mode, darker in dark mode.
public enum TonePolarity
{
    Darker,
    Lighter,
    Nearer,
    Farther,
}

public sealed class ToneDeltaPair(DynamicColor roleA, DynamicColor roleB, double delta, TonePolarity polarity, bool stayTogether)
{
    public DynamicColor RoleA { get; } = roleA;

    public DynamicColor RoleB { get; } = roleB;

    public double Delta { get; } = delta;

    public TonePolarity Polarity { get; } = polarity;

    // Whether both roles stay on the same side of the T50-59 awkward zone.
    public bool StayTogether { get; } = stayTogether;
}
