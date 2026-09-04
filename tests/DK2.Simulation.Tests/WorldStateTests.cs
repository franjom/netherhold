// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation;

namespace DK2.Simulation.Tests;

public class WorldStateTests
{
    [Fact]
    public void FromMap_SeedsTileHealthFromTerrainDefinition()
    {
        GameDatabase database = TestWorld.Database();
        WorldState world = TestWorld.World(database);

        // Interior is dirt path (100 hp in the fixture); the ring is rock (165).
        Assert.Equal(TestWorld.DirtPath, world.Tile(5, 5).TerrainId);
        Assert.Equal(database.TerrainById(TestWorld.DirtPath).StartingHealth, world.Tile(5, 5).Health);

        Assert.Equal(TestWorld.Rock, world.Tile(1, 5).TerrainId);
        Assert.Equal(database.TerrainById(TestWorld.Rock).StartingHealth, world.Tile(1, 5).Health);
    }

    [Fact]
    public void FromMap_CarriesOwnershipAndFlagsVerbatim()
    {
        GameDatabase database = TestWorld.Database();
        LevelMap map = TestWorld.Map();
        WorldState world = WorldState.FromMap(database, map, [(byte)0, (byte)1]);

        for (int y = 0; y < map.Height; y++)
            for (int x = 0; x < map.Width; x++)
            {
                Assert.Equal(map[x, y].PlayerId, world.Tile(x, y).OwnerPlayerId);
                Assert.Equal(map[x, y].Flags, world.Tile(x, y).Flags);
            }
    }

    [Fact]
    public void Tile_IsWritableThroughReference()
    {
        WorldState world = TestWorld.World(TestWorld.Database());
        world.Tile(3, 3).Health = 42;
        Assert.Equal(42, world.Tile(3, 3).Health);
    }

    [Fact]
    public void Players_AreOrderedById_SoHashingIsStable()
    {
        GameDatabase database = TestWorld.Database();
        WorldState ascending = WorldState.FromMap(database, TestWorld.Map(), [(byte)1, (byte)2, (byte)3]);
        WorldState descending = WorldState.FromMap(database, TestWorld.Map(), [(byte)3, (byte)2, (byte)1]);

        Hash64 a = Hash64.Start();
        Hash64 b = Hash64.Start();
        ascending.HashInto(ref a);
        descending.HashInto(ref b);

        Assert.Equal(a.Value, b.Value);
    }

    [Fact]
    public void Tile_RejectsOutOfBounds()
    {
        WorldState world = TestWorld.World(TestWorld.Database());
        Assert.Throws<ArgumentOutOfRangeException>(() => world.Tile(-1, 0).Health);
        Assert.Throws<ArgumentOutOfRangeException>(() => world.Tile(0, world.Height).Health);
        Assert.False(world.Contains(world.Width, 0));
    }

    [Fact]
    public void Tick_AdvancesExactlyOne()
    {
        GameSimulation sim = TestWorld.Simulation();
        Assert.Equal(GameTick.Zero, sim.CurrentTick);
        sim.Tick();
        Assert.Equal(new GameTick(1), sim.CurrentTick);
        sim.Run(9);
        Assert.Equal(new GameTick(10), sim.CurrentTick);
    }
}
