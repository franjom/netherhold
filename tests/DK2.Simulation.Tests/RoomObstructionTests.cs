// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Commands;
using DK2.Simulation.Navigation;

namespace DK2.Simulation.Tests;

/// <summary>What stands on a tile and stops a room tile going down there: <c>0x0044F800</c>, asked by
/// the placement predicate <c>0x0044F630</c> (Q501), as <see cref="RoomRules.IsObstructed"/> mirrors it.</summary>
public class RoomObstructionTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private const byte Rival = 4;
    private const byte GoblinId = 7;

    private static readonly TilePos Floor = new(3, 3);

    private static GameDatabase Db => Dk2TerrainFixture.Database();

    private static WorldState World(GameDatabase db)
    {
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Neutral, Keeper, Rival]);
        Claim(db, world, Floor);
        return world;
    }

    private static void Claim(GameDatabase db, WorldState world, TilePos at)
    {
        ref TileState tile = ref world.Tile(at);
        tile.TerrainId = Dk2TerrainFixture.ClaimedPath;
        tile.OwnerPlayerId = Keeper;
        tile.Health = db.TerrainById(Dk2TerrainFixture.ClaimedPath).StartingHealth;
    }

    private static RoomPlacement Treasury(GameDatabase db, WorldState world) =>
        RoomRules.CanBuild(db, world, Floor, db.Room(RoomTypeId.Treasury), Keeper);

    [Fact]
    public void ARivalsCreatureObstructsAndTheKeepersOwnDoesNot()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        CreatureState goblin = world.SpawnCreature(db, GoblinId, Keeper, Floor);
        Assert.Equal(RoomPlacement.Allowed, Treasury(db, world));

        goblin.OwnerPlayerId = Rival;
        Assert.Equal(RoomPlacement.Obstructed, Treasury(db, world));
    }

    /// <summary>The owner is compared raw (<c>+0x28</c> against the tag), so an ally's creature obstructs.</summary>
    [Fact]
    public void AnAllysCreatureObstructs()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        world.SetAllied(Keeper, Rival, true);
        world.SpawnCreature(db, GoblinId, Rival, Floor);

        Assert.True(world.AreAllied(Keeper, Rival));
        Assert.Equal(RoomPlacement.Obstructed, Treasury(db, world));
    }

    [Fact]
    public void ARivalsImpDoesNotObstruct()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        world.SpawnCreature(db, Dk2TerrainFixture.ImpCreatureId, Rival, Floor);

        Assert.Equal(RoomPlacement.Allowed, Treasury(db, world));
    }

    /// <summary>DK2 tests <c>CreatureDying</c> (<c>+0x3E6</c> bit 21); ours is
    /// <see cref="CreatureDeathRules.IsDown"/>, which also covers a creature out of health and not yet
    /// booked as collapsed, at most a game second wider (<see cref="Systems.DoorSystem.Eligible"/>).
    /// This pins that wider window, not the bit.</summary>
    [Fact]
    public void ARivalsCreatureOutOfHealthDoesNotObstruct_IsDownStandsInForCreatureDying()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        CreatureState goblin = world.SpawnCreature(db, GoblinId, Rival, Floor);
        goblin.Health = 0;

        Assert.Equal(RoomPlacement.Allowed, Treasury(db, world));
    }

    /// <summary>Classes 3 and 4 obstruct with no owner test.</summary>
    [Theory]
    [InlineData(Keeper)]
    [InlineData(Rival)]
    public void ATrapObstructsWhoeverOwnsIt(byte owner)
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        ThingProjection.Place(world, db, [new TrapThing(new byte[16])
        {
            X = Floor.X, Y = Floor.Y, TrapId = Dk2TerrainFixture.SentryTrapId, PlayerId = owner, NumberOfShots = 1,
        }]);

        Assert.Equal(RoomPlacement.Obstructed, Treasury(db, world));
    }

    [Theory]
    [InlineData(Keeper)]
    [InlineData(Rival)]
    public void ADoorObstructsWhoeverOwnsIt(byte owner)
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        ThingProjection.Place(world, db, [new DoorThing(new byte[20])
        {
            X = Floor.X, Y = Floor.Y, DoorId = 1, PlayerId = owner, Flags = 0, Unknown_0011 = new byte[3],
        }]);

        Assert.Equal(RoomPlacement.Obstructed, Treasury(db, world));
    }

    /// <summary>The walk skips the object class without reading a definition, so an Obstacle does
    /// not obstruct: OpenKeeper's <c>c047a8b1</c> blocks on it, DK2 does not.</summary>
    [Fact]
    public void AnObstacleObjectDoesNotObstruct()
    {
        GameDatabase db = Db;
        WorldState world = World(db);
        world.SpawnObject(db, WorldSteering.DungeonHeartObstacleObjectId, Keeper, world.TileIndex(Floor));
        Assert.True(db.ObjectById(WorldSteering.DungeonHeartObstacleObjectId)!.Flags.HasFlag(ObjectFlags.Obstacle));

        Assert.Equal(RoomPlacement.Allowed, Treasury(db, world));
    }

    // ---- a bridge: the seeds are tested, the tiles a span spreads to are not (0x004C57F0) ----

    private static (GameSimulation Sim, ListEventSink Events) Shore(GameDatabase db, TilePos a, TilePos b)
    {
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Neutral, Keeper, Rival]);
        foreach (TilePos at in new[] { a, b })
        {
            ref TileState water = ref world.Tile(at);
            water.TerrainId = Dk2TerrainFixture.Water;
            water.Health = db.TerrainById(Dk2TerrainFixture.Water).StartingHealth;
        }
        // Own floor west of the first tile only: the second touches none, so only a spread reaches it.
        Claim(db, world, new TilePos(a.X - 1, a.Y));
        RoomRules.Place(db, ref world.Tile(1, 1), db.Room(RoomTypeId.DungeonHeart), Keeper);
        GoldStore.Deposit(world, db, Keeper, 1000);

        var sim = new GameSimulation(db, world, 3, Dk2TerrainFixture.DefaultSystems());
        var sink = new ListEventSink();
        sim.ObserveWith(sink);
        return (sim, sink);
    }

    [Fact]
    public void ABridgeTileARivalStandsOnIsRefusedAlone()
    {
        GameDatabase db = Db;
        TilePos near = new(6, 6), far = new(7, 6);
        var (sim, events) = Shore(db, near, far);
        sim.World.SpawnCreature(db, GoblinId, Rival, near);

        sim.Tick(InputFrame.Of(BuildRoomCommand.At(Keeper, RoomTypeId.WoodenBridge, near)));

        Assert.Equal(Dk2TerrainFixture.Water, sim.World.Tile(near).TerrainId);
        Assert.Contains(events.Events, e => e.Kind == "built" && e.Detail.Contains("refused=Obstructed=1"));
    }

    /// <summary>The owner bridged over a hero standing in water as part of a longer drag (2026-10-23):
    /// the hero's tile is reached by the spread, which never asks the predicate.</summary>
    [Fact]
    public void ADraggedSpanBridgesOverARivalOnATileItSpreadsTo()
    {
        GameDatabase db = Db;
        TilePos near = new(6, 6), far = new(7, 6);
        var (sim, _) = Shore(db, near, far);
        sim.World.SpawnCreature(db, GoblinId, Rival, far);

        sim.Tick(InputFrame.Of(BuildRoomCommand.At(Keeper, RoomTypeId.WoodenBridge, near, far)));

        Assert.Equal(Dk2TerrainFixture.WoodenBridgeTerrain, sim.World.Tile(near).TerrainId);
        Assert.Equal(Dk2TerrainFixture.WoodenBridgeTerrain, sim.World.Tile(far).TerrainId);
    }

    /// <summary>A seed a rival stands on is not marked, and nothing else in the drag touches own
    /// floor, so the whole span is refused.</summary>
    [Fact]
    public void ADraggedSpanWhoseOnlySeedIsObstructedLaysNothing()
    {
        GameDatabase db = Db;
        TilePos near = new(6, 6), far = new(7, 6);
        var (sim, _) = Shore(db, near, far);
        sim.World.SpawnCreature(db, GoblinId, Rival, near);

        sim.Tick(InputFrame.Of(BuildRoomCommand.At(Keeper, RoomTypeId.WoodenBridge, near, far)));

        Assert.Equal(Dk2TerrainFixture.Water, sim.World.Tile(near).TerrainId);
        Assert.Equal(Dk2TerrainFixture.Water, sim.World.Tile(far).TerrainId);
    }
}
