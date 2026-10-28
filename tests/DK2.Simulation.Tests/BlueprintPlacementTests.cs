// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Commands;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>A keeper placing a door or trap blueprint during play (<c>0x004C33B0</c>,
/// <c>0x004C3860</c>; ADR-0231, <c>blueprint-placement.md</c>).</summary>
public sealed class BlueprintPlacementTests
{
    private const byte Keeper = 4;
    private const byte Rival = 5;
    private const byte WoodenDoor = 1, Barricade = 6, Alarm = 7;

    // A corridor at y = 4 between walls at y = 3 and y = 5, from x = 2 to x = 8; open floor elsewhere.
    private static readonly TilePos InTheCorridor = new(5, 4);
    private static readonly TilePos OpenFloor = new(5, 7);

    private static (GameDatabase, WorldState) World(int gold = 10_000)
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Neutral, Keeper, Rival]);
        for (int y = 1; y <= 8; y++)
        for (int x = 1; x <= 10; x++)
        {
            ref TileState tile = ref world.Tile(x, y);
            tile.TerrainId = y is 3 or 5 ? Dk2TerrainFixture.Rock : Dk2TerrainFixture.ClaimedPath;
            tile.OwnerPlayerId = y is 3 or 5 ? (byte)0 : Keeper;
        }
        world.Tile(1, 8).GoldHeld = gold;
        return (db, world);
    }

    [Fact]
    public void ADoorBetweenTwoWallsIsLaid_PaidFor_Ordered_AndListed()
    {
        (GameDatabase db, WorldState world) = World();
        int before = GoldStore.Total(world, Keeper);

        Assert.Equal(BlueprintPlacementRules.Verdict.Allowed, BlueprintPlacementRules.PlaceDoor(world, db, InTheCorridor, Keeper, WoodenDoor));

        DoorState door = Assert.Single(world.Doors);
        Assert.Equal((InTheCorridor.X, InTheCorridor.Y, WoodenDoor, Keeper, true), (door.X, door.Y, door.DoorId, door.PlayerId, door.UnderConstruction));
        Assert.Equal(before - db.Doors[WoodenDoor].GoldCost, GoldStore.Total(world, Keeper));
        ManufactureOrder order = Assert.Single(world.Player(Keeper).ManufactureQueue);
        Assert.Equal((CrateRules.DoorKind, WoodenDoor), (order.Kind, order.TypeId));
        Assert.Equal([new BlueprintRef(CrateRules.DoorKind, world.Doors[0].Id)], world.Player(Keeper).Blueprints);
    }

    /// <summary>A door needs walls on two opposite sides; a Barricade does not.</summary>
    [Fact]
    public void ADoorOnOpenFloorIsRefused_ABarricadeIsNot()
    {
        (GameDatabase db, WorldState world) = World();

        Assert.Equal(BlueprintPlacementRules.Verdict.NoWalls, BlueprintPlacementRules.PlaceDoor(world, db, OpenFloor, Keeper, WoodenDoor));
        Assert.Equal(BlueprintPlacementRules.Verdict.Allowed, BlueprintPlacementRules.PlaceDoor(world, db, OpenFloor, Keeper, Barricade));
    }

    /// <summary>The orientation: 0 between north and south walls, 1 between west and east ones,
    /// 2 for a Barricade even between walls, and 1 walled on all four sides, since west and east are
    /// tested first (<c>0x004C33B0</c>).</summary>
    [Fact]
    public void ADoorsOrientationNamesTheWallsItStandsBetween()
    {
        (GameDatabase db, WorldState world) = World();
        world.Tile(4, 7).TerrainId = Dk2TerrainFixture.Rock;
        world.Tile(6, 7).TerrainId = Dk2TerrainFixture.Rock;
        world.Tile(7, 4).TerrainId = Dk2TerrainFixture.Rock;
        world.Tile(9, 4).TerrainId = Dk2TerrainFixture.Rock;

        BlueprintPlacementRules.PlaceDoor(world, db, InTheCorridor, Keeper, WoodenDoor);
        BlueprintPlacementRules.PlaceDoor(world, db, OpenFloor, Keeper, WoodenDoor);
        BlueprintPlacementRules.PlaceDoor(world, db, new TilePos(3, 4), Keeper, Barricade);
        BlueprintPlacementRules.PlaceDoor(world, db, new TilePos(8, 4), Keeper, WoodenDoor);

        Assert.Equal([(byte)0, (byte)1, (byte)2, (byte)1], world.Doors.Select(d => d.Orientation));
    }

    /// <summary>The level loader <c>0x00520F40</c> gives an authored door the same orientation.</summary>
    [Fact]
    public void AnAuthoredDoorIsOrientedAsAPlacedOneIs()
    {
        (GameDatabase db, WorldState world) = World();

        ThingProjection.Place(world, db, [new DoorThing(new byte[20]) { X = InTheCorridor.X, Y = InTheCorridor.Y, PlayerId = Keeper, DoorId = WoodenDoor, Unknown_0011 = new byte[3] }]);

        Assert.Equal(0, Assert.Single(world.Doors).Orientation);
    }

    [Fact]
    public void ATrapGoesOnTheKeepersOpenFloor_AndNowhereElse()
    {
        (GameDatabase db, WorldState world) = World();
        ref TileState rival = ref world.Tile(8, 7);
        rival.OwnerPlayerId = Rival;
        RoomRules.Place(db, ref world.Tile(2, 7), db.Room(RoomTypeId.Treasury), Keeper);

        Assert.Equal(BlueprintPlacementRules.Verdict.Allowed, BlueprintPlacementRules.PlaceTrap(world, db, OpenFloor, Keeper, Alarm));
        Assert.Equal(BlueprintPlacementRules.Verdict.Occupied, BlueprintPlacementRules.PlaceTrap(world, db, OpenFloor, Keeper, Alarm));
        Assert.Equal(BlueprintPlacementRules.Verdict.NotTheKeepersTile, BlueprintPlacementRules.PlaceTrap(world, db, new TilePos(8, 7), Keeper, Alarm));
        Assert.Equal(BlueprintPlacementRules.Verdict.ARoom, BlueprintPlacementRules.PlaceTrap(world, db, new TilePos(2, 7), Keeper, Alarm));
        world.Tile(6, 7).TerrainId = Dk2TerrainFixture.Rock;
        Assert.Equal(BlueprintPlacementRules.Verdict.Solid, BlueprintPlacementRules.PlaceTrap(world, db, new TilePos(6, 7), Keeper, Alarm));
        Assert.Single(world.Traps);
        Assert.Equal([new BlueprintRef(CrateRules.TrapKind, world.Traps[0].Id)], world.Player(Keeper).Blueprints);
    }

    /// <summary><c>0x005066E0</c> counts the keeper's blueprints of the type against its crates of
    /// the type (door arm <c>0x00506825..0x00506890</c>) and never reads the ring. An order whose
    /// blueprint was destroyed stays queued (Q668), so the next blueprint of that type still orders
    /// its own crate.</summary>
    [Fact]
    public void AnOrderLeftByADestroyedBlueprintIsNotCountedAsACrate()
    {
        (GameDatabase db, WorldState world) = World();
        ManufactureRules.Enqueue(ref world.Player(Keeper),
            new ManufactureOrder(CrateRules.TrapKind, Alarm, db.Traps[Alarm].ManufactureRequired));

        Assert.Equal(BlueprintPlacementRules.Verdict.Allowed, BlueprintPlacementRules.PlaceTrap(world, db, OpenFloor, Keeper, Alarm));

        Assert.Equal(2, world.Player(Keeper).ManufactureQueue.Count(o => o.Kind == CrateRules.TrapKind && o.TypeId == Alarm));
    }

    [Fact]
    public void AKeeperWhoCannotPayGetsNothing()
    {
        (GameDatabase db, WorldState world) = World(gold: 100);

        Assert.Equal(BlueprintPlacementRules.Verdict.NotEnoughGold, BlueprintPlacementRules.PlaceTrap(world, db, OpenFloor, Keeper, Alarm));
        Assert.Empty(world.Traps);
        Assert.Empty(world.Player(Keeper).ManufactureQueue);
        Assert.Equal(100, GoldStore.Total(world, Keeper));
    }

    [Fact]
    public void TheCommandPlacesItAndSaysSo()
    {
        (GameDatabase db, WorldState world) = World();
        var sim = new GameSimulation(db, world, seed: 1, [new BlueprintSystem()]);
        var sink = new ListEventSink();
        sim.ObserveWith(sink);

        sim.Tick(InputFrame.Of(new PlaceDoorCommand(Keeper, InTheCorridor, WoodenDoor), new PlaceDoorCommand(Keeper, OpenFloor, WoodenDoor)));

        Assert.Contains(sink.Events, e => e.Kind == "blueprint_placed" && e.Detail.Contains("kind=door"));
        Assert.Contains(sink.Events, e => e.Kind == "blueprint_refused" && e.Detail.Contains("reason=NoWalls"));
        Assert.Single(world.Doors);
    }
}
