// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;

namespace DK2.Presentation.Tests;

public class TerrainLightingViewTests
{
    [Theory]
    [InlineData(false, false, 0)]
    [InlineData(true, false, 265)]
    [InlineData(true, true, 0)]
    public void ViewExposesOnlyEnabledAndVisibleAmbientColour(bool enabled, bool hidden, int red)
    {
        TerrainFlags flags = TerrainFlags.Ownable | TerrainFlags.AmbientColorRed;
        if (enabled) flags |= TerrainFlags.AmbientLight;
        TerrainDef terrain = Definition(1, "Path", flags) with { Lighting = new(1, 2, 3, 10, 20, 30) };
        var database = new GameDatabase([terrain, Definition(27, "Fog Of War", TerrainFlags.Solid)],
            [], [], new GlobalVariables());
        var world = WorldState.FromMap(database, new LevelMap(1, 1, [new MapTile(1, 1, 0, 0)]),
            [1], fogOfWarEnabled: hidden);
        TileView tile = new WorldView(database, 1).TileAt(world, 0, 0);
        Assert.Equal(new TerrainLightColour(red, red == 0 ? 0 : 20, red == 0 ? 0 : 30), tile.AmbientTint);
    }

    private static TerrainDef Definition(byte id, string name, TerrainFlags flags) => new()
    {
        Name = name, TerrainId = id, Flags = flags, StartingHealth = 1, MaxHealth = 1,
        GoldValue = 0, ManaGain = 0, MaxManaGain = 0, Damage = 0,
        MaxHealthTypeTerrainId = id, DestroyedTypeTerrainId = id, Depth = new Fp32(0),
        NameStringId = 0, TooltipStringId = 0,
    };
}
