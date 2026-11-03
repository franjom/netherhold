// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using System.Numerics;
using DK2.Core;
using DK2.Data;
using DK2.Data.Model;
using DK2.Simulation;
using DK2.TestSupport;

namespace DK2.Presentation.Tests;

/// <summary>The terrain ambience grid of <c>EffectGrid_UpdateAroundListener</c> (<c>0x0044E2A0</c>): an 11-by-11 grid of the tiles around the
/// listener, each cell the sound category of the tile's terrain (<c>+0x195</c>; the Temple's waterless category <c>+0x199</c> is not asked) when
/// the tile is explored, else none; the service's ambient pass (<c>0x00609480</c>, <c>0x00609A30</c>) plays group <c>0x2EA</c> in each category present
/// whose map has that group, at a point it derives from the cells. Docs: sound-effects-continuous.md section 4.</summary>
public sealed class AmbienceGridTests
{
    private static readonly PlayerId Viewer = new(PlayerIds.FirstKeeper);

    [InstallFact]
    public void TheGridIsElevenBySquareOfTheExploredTilesCategoriesAroundTheListener()
    {
        string install = Dk2InstallGate.Path;
        GameDatabase database = GameDatabaseLoader.Load(install);
        WorldState world = WorldState.FromMap(database, GameDatabaseLoader.LoadCampaignMap(install, "Level1")!, [0, 1]);
        byte water = database.WaterTerrain!.TerrainId;
        var listener = new TilePos(20, 20);
        for (int y = 0; y < world.Height; y++)
            for (int x = 0; x < world.Width; x++) world.Tile(x, y).ClearExploredBy(Viewer);
        world.Tile(20, 20).TerrainId = water;
        world.Tile(20, 20).SetExploredBy(Viewer);
        world.Tile(25, 25).TerrainId = water;     // the corner of the window
        world.Tile(25, 25).SetExploredBy(Viewer);
        world.Tile(26, 20).TerrainId = water;     // one outside it
        world.Tile(26, 20).SetExploredBy(Viewer);
        world.Tile(21, 20).TerrainId = water;     // inside, but not explored
        world.Tile(21, 20).ClearExploredBy(Viewer);

        string[] cells = AmbienceGrid.Cells(world, database, Viewer, listener);

        Assert.Equal(AmbienceGrid.Side * AmbienceGrid.Side, cells.Length);
        string category = database.TerrainById(water).SoundCategory;
        Assert.Equal("TERRAIN_WATER", category);
        Assert.Equal(category, cells[(5 * AmbienceGrid.Side) + 5]);                       // the listener's own tile
        Assert.Equal(category, cells[(10 * AmbienceGrid.Side) + 10]);                     // (25, 25)
        Assert.Equal(2, cells.Count(c => c == category));
        Assert.Equal(AmbienceGrid.Side * AmbienceGrid.Side - 2, cells.Count(c => c.Length == 0 || c != category));
    }

    [Fact]
    public void ACategoryWithAnAmbientGroupGetsOneEmitterAtTheCentreOfItsCellsInViewSpace()
    {
        var cells = new string[AmbienceGrid.Side * AmbienceGrid.Side];
        Array.Fill(cells, "");
        cells[(5 * AmbienceGrid.Side) + 3] = "TERRAIN_WATER";    // two cells left of the listener's column
        cells[(5 * AmbienceGrid.Side) + 5] = "TERRAIN_WATER";    // the listener's own
        cells[(2 * AmbienceGrid.Side) + 8] = "TERRAIN_DIRT_PATH";  // no ambient group: no emitter

        List<AmbienceGrid.Emitter> emitters = AmbienceGrid.Emitters(cells, new TilePos(20, 30), category => category == "TERRAIN_WATER");

        AmbienceGrid.Emitter water = Assert.Single(emitters);
        Assert.Equal("TERRAIN_WATER", water.Category);
        // Cells (18, 30) and (20, 30): their centres' mean, (19.5 + 0.5 .. ) = 19.5, y 30.5; view space (x, 0, -y).
        Assert.Equal(new Vector3(19.5f, 0f, -30.5f), water.Position);
    }

    [Fact]
    public void AnEmptyGridHasNoEmitters()
    {
        var cells = new string[AmbienceGrid.Side * AmbienceGrid.Side];
        Array.Fill(cells, "");
        Assert.Empty(AmbienceGrid.Emitters(cells, new TilePos(5, 5), _ => true));
    }
}
