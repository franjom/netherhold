// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

namespace DK2.Data.Model;

/// <summary>Native added-light channels, in 0..510 units (classic-lighting.md).</summary>
public readonly record struct TerrainLightColour(int Red, int Green, int Blue);

/// <summary>Authored RGB bytes at terrain file 0x1DB and 0x201; see classic-lighting.md.</summary>
public readonly record struct TerrainLighting(
    byte TerrainRed, byte TerrainGreen, byte TerrainBlue,
    byte AmbientRed, byte AmbientGreen, byte AmbientBlue)
{
    public TerrainLightColour AmbientColour(TerrainFlags flags) => new(
        AmbientRed + ((flags & TerrainFlags.AmbientColorRed) != 0 ? 255 : 0),
        AmbientGreen + ((flags & TerrainFlags.AmbientColorGreen) != 0 ? 255 : 0),
        AmbientBlue + ((flags & TerrainFlags.AmbientColorBlue) != 0 ? 255 : 0));

    public TerrainLightColour TerrainColour(TerrainFlags flags) => new(
        TerrainRed + ((flags & TerrainFlags.TerrainColorRed) != 0 ? 255 : 0),
        TerrainGreen + ((flags & TerrainFlags.TerrainColorGreen) != 0 ? 255 : 0),
        TerrainBlue + ((flags & TerrainFlags.TerrainColorBlue) != 0 ? 255 : 0));
}
