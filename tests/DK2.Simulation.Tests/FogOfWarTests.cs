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
    public void CapturingARoomExploresEveryTileForTheNewOwnerWithoutScanning()
    {
        (GameDatabase db, WorldState world, _) = FogWorld();
        for (int x = 8; x <= 10; x++)
            RoomRules.Place(db, ref world.Tile(x, 8), db.Room(RoomTypeId.Treasury), 2);
        RoomInstance room = RoomIndex.RoomAt(world, db, 8 * world.Width + 8)!;

        RoomHealthRules.Apply(world, db, room, -RoomHealthRules.Health(world, db, room), 1);

        for (int x = 8; x <= 10; x++)
        {
            Assert.True(world.Tile(x, 8).IsExploredBy(1));
            Assert.True(world.Tile(x, 8).IsExploredBy(2));
            Assert.False(world.Tile(x, 8).IsDiscoveredBy(1));
        }
    }

    [Fact]
    public void ExploringOrReshroudingAPortalChangesTheWholeRoomWithoutScanning()
    {
        (GameDatabase db, WorldState world, _) = FogWorld();
        for (int x = 8; x <= 10; x++)
            RoomRules.Place(db, ref world.Tile(x, 8), db.Room(RoomTypeId.Portal), 2);

        DiscoveryRules.SetExplored(world, db, new TilePos(9, 8), 1);
        for (int x = 8; x <= 10; x++)
        {
            Assert.True(world.Tile(x, 8).IsExploredBy(1));
            Assert.False(world.Tile(x, 8).IsDiscoveredBy(1));
        }
        DiscoveryRules.SetExplored(world, db, new TilePos(9, 8), 1, explored: false);
        for (int x = 8; x <= 10; x++)
        {
            Assert.False(world.Tile(x, 8).IsExploredBy(1));
            Assert.True(world.Tile(x, 8).IsExploredBy(2));
        }
    }

    [Fact]
    public void ExploredAndScannedBitsAffectHashIndependently()
    {
        (GameDatabase db, WorldState world, _) = FogWorld();
        var simulation = new GameSimulation(db, world, 9);
        ulong original = simulation.StateHash();
        world.Tile(10, 10).SetExploredBy(1);
        ulong explored = simulation.StateHash();
        Assert.NotEqual(original, explored);
        world.Tile(10, 10).ClearExploredBy(1);
        Assert.Equal(original, simulation.StateHash());
        world.Tile(10, 10).SetDiscoveredBy(1);
        Assert.NotEqual(original, simulation.StateHash());
        Assert.NotEqual(explored, simulation.StateHash());
    }

    [Fact]
    public void OwnedTerrainStartsExploredWithoutBeingScanned()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(db,
            new LevelMap(1, 1, [new MapTile(Dk2TerrainFixture.ClaimedPath, 3, 0, 0)]), [3], true);
        Assert.True(world.Tile(0, 0).IsExploredBy(3));
        Assert.False(world.Tile(0, 0).IsDiscoveredBy(3));
    }

    [Fact]
    public void FlaggedActionPointsExploreTheirInclusiveRectangleForEachViewer()
    {
        (GameDatabase db, WorldState world, _) = FogWorld();
        ThingProjection.Place(world, db,
        [new ActionPointThing(new byte[56])
        {
            StartX = 8, StartY = 8, EndX = 9, EndY = 9,
            ActionPointId = 1, Flags = 0x10, Name = "reveal",
        }]);
        Assert.True(world.Tile(8, 8).IsExploredBy(1));
        Assert.True(world.Tile(9, 9).IsExploredBy(1));
        Assert.True(world.Tile(9, 9).IsExploredBy(0));
        Assert.False(world.Tile(10, 9).IsExploredBy(1));
        Assert.False(world.Tile(9, 9).IsDiscoveredBy(1));
    }

    [Theory]
    [InlineData(0, true)]
    [InlineData(1, false)]
    public void TriggerSeventeenChangesExploredWithoutChangingScan(byte available, bool explored)
    {
        (GameDatabase db, WorldState world, _) = FogWorld();
        ThingProjection.Place(world, db,
        [new ActionPointThing(new byte[56])
        {
            StartX = 8, StartY = 8, EndX = 9, EndY = 9,
            ActionPointId = 1, Flags = 0x10, Name = "reveal",
        }]);
        world.Tile(8, 8).SetDiscoveredBy(1);
        var record = new ActionTriggerRecord(new byte[16])
        {
            Body = new byte[8], TriggerId = ushort.MaxValue, Type = 17,
            Parameters = new Dictionary<string, long> { ["ActionPointId"] = 1, ["Available"] = available },
        };
        TriggerRunResult result = new TriggerExecutor(new TriggerTree([record], ushort.MaxValue),
            world.Triggers, world, db).Run(1);
        Assert.Empty(result.Partial);
        Assert.Equal(explored, world.Tile(8, 8).IsExploredBy(1));
        Assert.True(world.Tile(8, 8).IsDiscoveredBy(1));
    }

    [Fact]
    public void CreatureSightExploresNearbyGroundButKeepsTheWiderScanSeparate()
    {
        (GameDatabase db, WorldState world, _) = FogWorld();
        OpenInterior(world);
        new GameSimulation(db, world, 1, [new FogOfWarSystem()]).Tick();
        Assert.True(world.Tile(10, 10).IsExploredBy(1));
        Assert.True(world.Tile(14, 10).IsExploredBy(1));
        Assert.True(world.Tile(15, 10).IsDiscoveredBy(1));
        Assert.False(world.Tile(15, 10).IsExploredBy(1));
        Assert.False(world.Tile(10, 10).IsExploredBy(2));
    }

    [Fact]
    public void SolidTerrainIsExploredButStopsSightBeyondItWithoutStoppingScan()
    {
        (GameDatabase db, WorldState world, _) = FogWorld();
        OpenInterior(world);
        for (int y = 0; y < world.Height; y++) world.Tile(11, y).TerrainId = 1;
        new GameSimulation(db, world, 1, [new FogOfWarSystem()]).Tick();
        Assert.True(world.Tile(11, 10).IsExploredBy(1));
        Assert.False(world.Tile(12, 10).IsExploredBy(1));
        Assert.True(world.Tile(12, 10).IsDiscoveredBy(1));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void PrisonersDoNotScanTerrainForTheirOwner(int prisonOwner)
    {
        (GameDatabase db, WorldState world, CreatureState creature) = FogWorld();
        creature.PrisonOwnerPlayerId = prisonOwner;
        new GameSimulation(db, world, 1, [new FogOfWarSystem()]).Tick();
        Assert.False(world.Tile(10, 10).IsDiscoveredBy(1));
        Assert.False(world.Tile(15, 10).IsDiscoveredBy(1));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AClosedDoorOnTheSourceTileStopsScanning(bool open)
    {
        (GameDatabase db, WorldState world, CreatureState creature) = FogWorld();
        ThingProjection.Place(world, db, [new DoorThing(new byte[20])
        {
            X = creature.Tile.X, Y = creature.Tile.Y, PlayerId = 1, DoorId = 1,
            Unknown_0011 = new byte[3],
        }]);
        world.Doors[0].Open = open;
        new GameSimulation(db, world, 1, [new FogOfWarSystem()]).Tick();
        Assert.Equal(open, world.Tile(10, 10).IsDiscoveredBy(1));
        Assert.Equal(open, world.Tile(15, 10).IsDiscoveredBy(1));
    }

    [Fact]
    public void NativePerTermRoundingIncludesThisNearBoundaryTile()
    {
        (GameDatabase db, WorldState world, CreatureState creature) = FogWorld();
        var target = new TilePos(10, 16);
        creature.PosX = CreatureState.CentreOf(target.X) - 914;
        creature.PosY = CreatureState.CentreOf(target.Y) - 24559;
        Assert.True(914L * 914 + 24559L * 24559 >= 24576L * 24576);
        new GameSimulation(db, world, 1, [new FogOfWarSystem()]).Tick();
        Assert.True(world.Tile(target).IsDiscoveredBy(1));
    }

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
        world.Tile(4, 3).SetExploredBy(2);
        var simulation = new GameSimulation(db, world, 9);

        GameSimulation restored = WorldSnapshot.FromBytes(WorldSnapshot.ToBytes(simulation), db);

        Assert.True(restored.World.FogOfWarEnabled);
        Assert.True(restored.World.Tile(3, 4).IsDiscoveredBy(1));
        Assert.True(restored.World.Tile(4, 3).IsExploredBy(2));
        Assert.False(restored.World.Tile(4, 3).IsDiscoveredBy(2));
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

    private static void OpenInterior(WorldState world)
    {
        for (int y = 1; y < world.Height - 1; y++)
            for (int x = 1; x < world.Width - 1; x++)
            {
                world.Tile(x, y).TerrainId = Dk2TerrainFixture.DirtPath;
                world.Tile(x, y).ExploredBy = PlayerMask.None;
            }
    }
}
