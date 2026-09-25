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
        Assert.Equal([new BlueprintRef(CrateRules.DoorKind, 0)], world.Player(Keeper).Blueprints);
    }

    /// <summary>A door needs walls on two opposite sides; a Barricade does not.</summary>
    [Fact]
    public void ADoorOnOpenFloorIsRefused_ABarricadeIsNot()
    {
        (GameDatabase db, WorldState world) = World();

        Assert.Equal(BlueprintPlacementRules.Verdict.NoWalls, BlueprintPlacementRules.PlaceDoor(world, db, OpenFloor, Keeper, WoodenDoor));
        Assert.Equal(BlueprintPlacementRules.Verdict.Allowed, BlueprintPlacementRules.PlaceDoor(world, db, OpenFloor, Keeper, Barricade));
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
        Assert.Equal([new BlueprintRef(CrateRules.TrapKind, 0)], world.Player(Keeper).Blueprints);
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
