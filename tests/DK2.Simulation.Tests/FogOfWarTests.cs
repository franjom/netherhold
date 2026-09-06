// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Commands;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

public sealed class FogOfWarTests
{
    [Fact]
    public void CreatureRevealsTheStrictCircularRangeFromItsDefinition()
    {
        (GameDatabase db, WorldState world, CreatureState creature) = FogWorld();
        var simulation = new GameSimulation(db, world, 1, [new FogOfWarSystem()]);

        simulation.Tick();

        Assert.True(world.Tile(15, 10).IsDiscoveredBy(1));
        Assert.True(world.Tile(14, 14).IsDiscoveredBy(1));
        Assert.False(world.Tile(16, 10).IsDiscoveredBy(1));
        Assert.False(world.Tile(15, 14).IsDiscoveredBy(1));
        Assert.False(world.Tile(10, 10).IsDiscoveredBy(2));

        creature.PosX = CreatureState.CentreOf(2);
        creature.PosY = CreatureState.CentreOf(2);
        simulation.Tick();

        Assert.True(world.Tile(15, 10).IsDiscoveredBy(1));
        Assert.True(world.Tile(2, 2).IsDiscoveredBy(1));
    }

    [Fact]
    public void DiscoveryAndFogSettingSurviveSnapshotRoundTrip()
    {
        (GameDatabase db, WorldState world, _) = FogWorld();
        world.Tile(3, 4).SetDiscoveredBy(1);
        var simulation = new GameSimulation(db, world, 9);

        GameSimulation restored = WorldSnapshot.FromBytes(WorldSnapshot.ToBytes(simulation), db);

        Assert.True(restored.World.FogOfWarEnabled);
        Assert.True(restored.World.Tile(3, 4).IsDiscoveredBy(1));
        Assert.Equal(simulation.StateHash(), restored.StateHash());
    }

    [Fact]
    public void SpeculativeTagIsStoredUnderFogThenDroppedWhenTerrainCannotBeTagged()
    {
        (GameDatabase db, WorldState world, CreatureState creature) = FogWorld();
        var tagOnly = new GameSimulation(db, world, 2, [new TileTagSystem()]);
        var target = new TilePos(2, 2);

        tagOnly.Tick(InputFrame.Of(TagTilesCommand.Tag(1, target)));
        Assert.True(world.Tile(target).IsTaggedBy(1));

        creature.PosX = CreatureState.CentreOf(target.X);
        creature.PosY = CreatureState.CentreOf(target.Y);
        var reveal = new GameSimulation(db, world, 2, [new FogOfWarSystem()]);
        reveal.Tick();

        Assert.True(world.Tile(target).IsDiscoveredBy(1));
        Assert.False(world.Tile(target).IsTaggedBy(1));
        Assert.Equal(target, creature.Tile);
    }

    private static (GameDatabase Database, WorldState World, CreatureState Creature) FogWorld()
    {
        CreatureDef imp = Dk2TerrainFixture.Imp();
        GameDatabase database = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(
            database, Dk2TerrainFixture.Map(24, 24), [0, 1, 2], fogOfWarEnabled: true);
        CreatureState creature = world.SpawnCreature(
            database, imp.CreatureId, 1, new TilePos(10, 10));
        return (database, world, creature);
    }
}
