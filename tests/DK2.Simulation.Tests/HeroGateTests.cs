// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation.Tests;

/// <summary>The Good creature's gate step-off in <c>0x00519B20</c> (Q427,
/// hero-parties-and-effect-generators.md), on the map's own gates, whose slab chains run in
/// row-major order.</summary>
public class HeroGateTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;

    private static (GameDatabase db, WorldState world) Open()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(20, 20), [(byte)0, PlayerIds.Good, PlayerIds.Neutral, Keeper]);
        for (int y = 1; y < world.Height - 1; y++)
            for (int x = 1; x < world.Width - 1; x++)
                world.Tile(x, y).TerrainId = Dk2TerrainFixture.DirtPath;
        return (db, world);
    }

    private static void Paint(WorldState world, byte terrain, params (int X, int Y)[] tiles)
    {
        foreach ((int x, int y) in tiles)
        {
            ref TileState tile = ref world.Tile(x, y);
            tile.TerrainId = terrain;
            tile.OwnerPlayerId = PlayerIds.Good;
        }
    }

    [Fact]
    public void AHeroMadeOnA2x2GateStartsOneTileRightAndDownOfItsFirstSlab()
    {
        (GameDatabase db, WorldState world) = Open();
        Paint(world, Dk2TerrainFixture.HeroGate2By2Terrain, (5, 5), (6, 5), (5, 6), (6, 6));
        var at = new TilePos(5, 6);
        Assert.Equal(HeroPartyRules.StepOffResult.Moved, HeroPartyRules.StepOff(world, db, null, PlayerIds.Good, ref at, out _));
        Assert.Equal(new TilePos(6, 6), at);
    }

    [Fact]
    public void AHeroMadeOnA3x1GateStartsOnTheTileAfterItsFirstSlab()
    {
        (GameDatabase db, WorldState world) = Open();
        Paint(world, Dk2TerrainFixture.HeroGate3By1Terrain, (5, 5), (6, 5), (7, 5));
        var at = new TilePos(7, 5);
        Assert.Equal(HeroPartyRules.StepOffResult.Moved, HeroPartyRules.StepOff(world, db, null, PlayerIds.Good, ref at, out _));
        Assert.Equal(new TilePos(6, 5), at);
    }

    [Fact]
    public void OnlyTheGoodPlayersCreaturesStepOff()
    {
        (GameDatabase db, WorldState world) = Open();
        Paint(world, Dk2TerrainFixture.HeroGate3By1Terrain, (5, 5), (6, 5), (7, 5));
        var at = new TilePos(7, 5);
        Assert.Equal(HeroPartyRules.StepOffResult.None, HeroPartyRules.StepOff(world, db, null, Keeper, ref at, out _));
        Assert.Equal(new TilePos(7, 5), at);
    }

    /// <summary>The Tile gate's mask is built north, west, east, south but read through the
    /// north, east, south, west table: with only the west side open the creature is sent east —
    /// DK2's own mismatch, reproduced. With no side open nothing is made.</summary>
    [Fact]
    public void ATileGateWithOnlyItsWestSideOpenSendsTheHeroEast_AndOneWithNoneRefuses()
    {
        (GameDatabase db, WorldState world) = Open();
        Paint(world, Dk2TerrainFixture.HeroGateTileTerrain, (5, 5));
        foreach ((int x, int y) in new[] { (5, 4), (6, 5), (5, 6) }) world.Tile(x, y).TerrainId = Dk2TerrainFixture.Rock;

        var at = new TilePos(5, 5);
        Assert.Equal(HeroPartyRules.StepOffResult.Walks, HeroPartyRules.StepOff(world, db, new SimRandom(7), PlayerIds.Good, ref at, out TilePos side));
        Assert.Equal(new TilePos(5, 5), at);
        Assert.Equal(new TilePos(6, 5), side);

        world.Tile(4, 5).TerrainId = Dk2TerrainFixture.Rock;
        Assert.Equal(HeroPartyRules.StepOffResult.Refused, HeroPartyRules.StepOff(world, db, new SimRandom(7), PlayerIds.Good, ref at, out _));
    }
}
