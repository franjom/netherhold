// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation.Tests;

/// <summary>Action 11, <c>0x00497DF0</c>: a room knocked down tile by tile through the tile's own
/// health, and the hero who does it on stepping in (state 267). room-health.md §4–5, ADR-0339.</summary>
public class RoomAttackTests
{
    private const byte Keeper = 3;
    private const byte Rival = 4;

    private static readonly GameDatabase Db = Dk2TerrainFixture.Database();

    private static WorldState World() => WorldState.FromMap(
        Db, Dk2TerrainFixture.Map(), [(byte)0, PlayerIds.Good, PlayerIds.Neutral, Keeper, Rival]);

    private static void Paint(WorldState world, RoomTypeId type, int x0, int y, int width, byte owner)
    {
        for (int x = x0; x < x0 + width; x++)
            RoomRules.Place(Db, ref world.Tile(x, y), Db.Room(type), owner);
    }

    private static int Attack(WorldState world, int x, int y, byte playerId, int efficiency = EfficiencyRules.BaseModifier)
    {
        var sim = new GameSimulation(Db, world, seed: 1, [new Systems.TileWorkSystem()]);
        for (int i = 1; i <= 100; i++)
        {
            world.QueueWork(new TileWorkOrder((y * world.Width) + x, TileWorkKind.AttackRoom, playerId));
            sim.Tick(InputFrame.Empty);
            if (Db.RoomByTerrain(world.Tile(x, y).TerrainId) is null) return i;
        }
        throw new InvalidOperationException("the tile never fell");
    }

    [Fact]
    public void AHeroKnocksARoomTileDownInThreeBlows_ThroughTheTilesOwnHealth()
    {
        WorldState world = World();
        Paint(world, RoomTypeId.Treasury, 5, 5, 3, Keeper);
        RoomInstance before = RoomIndex.RoomAt(world, Db, (5 * world.Width) + 6)!;

        Assert.Equal(-3750, TileWorkRules.HeroAttackRoomPerApplication(Db));
        Assert.Equal(3, Attack(world, 6, 5, PlayerIds.Good));

        // Claimed Path, still the keeper's, and the two ends are two rooms at full health.
        Assert.Equal(Dk2TerrainFixture.ClaimedPath, world.Tile(6, 5).TerrainId);
        Assert.Equal(Keeper, world.Tile(6, 5).OwnerPlayerId);
        Assert.Equal(RoomHealthRules.Cap(Db, before) / 3, RoomHealthRules.Health(world, Db, RoomIndex.RoomAt(world, Db, (5 * world.Width) + 5)!));
        Assert.Equal(1, RoomIndex.RoomAt(world, Db, (5 * world.Width) + 7)!.TileCount);
    }

    [Fact]
    public void AnyoneElseUsesTheirWorkRate()
    {
        WorldState world = World();
        Paint(world, RoomTypeId.Treasury, 5, 5, 1, Keeper);

        // −1500 / 4 × 80% = −300 against 10000.
        Assert.Equal(34, Attack(world, 5, 5, Rival));
    }

    [Fact]
    public void OnlyABuildableRoomCanBeKnockedDown()
    {
        WorldState world = World();
        Paint(world, RoomTypeId.Treasury, 5, 5, 1, Keeper);
        Paint(world, RoomTypeId.Portal, 7, 5, 1, Keeper);

        Assert.True(TileWorkRules.Applies(Db, world.Tile(5, 5), TileWorkKind.AttackRoom, PlayerIds.Good));
        Assert.False(TileWorkRules.Applies(Db, world.Tile(7, 5), TileWorkKind.AttackRoom, PlayerIds.Good));
        Assert.False(TileWorkRules.Applies(Db, world.Tile(9, 9), TileWorkKind.AttackRoom, PlayerIds.Good));
    }

    /// <summary>The survivors keep the old room's share of health (<c>0x004ED440</c>).</summary>
    [Fact]
    public void TheRoomsThatAreLeftKeepItsShareOfHealth()
    {
        WorldState world = World();
        Paint(world, RoomTypeId.Treasury, 5, 5, 3, Keeper);
        RoomInstance room = RoomIndex.RoomAt(world, Db, (5 * world.Width) + 5)!;
        RoomHealthRules.Set(world, Db, room, RoomHealthRules.Cap(Db, room) / 2);

        Attack(world, 6, 5, PlayerIds.Good);

        int perTile = RoomHealthRules.MaxPerTile(Db, room.Definition);
        Assert.Equal(perTile / 2, RoomHealthRules.Health(world, Db, RoomIndex.RoomAt(world, Db, (5 * world.Width) + 5)!));
        Assert.Equal(perTile / 2, RoomHealthRules.Health(world, Db, RoomIndex.RoomAt(world, Db, (5 * world.Width) + 7)!));
    }

    [Fact]
    public void ATreasuryTileKnockedDownDropsItsGoldOnTheFloor_AndPaysNoRefund()
    {
        WorldState world = World();
        Paint(world, RoomTypeId.Treasury, 5, 5, 1, Keeper);
        GoldStore.Deposit(world, Db, Keeper, 1500);

        var sim = new GameSimulation(Db, world, seed: 1, [new Systems.TileWorkSystem()]);
        var sink = new ListEventSink();
        sim.ObserveWith(sink);
        for (int i = 0; i < 3; i++)
        {
            world.QueueWork(new TileWorkOrder((5 * world.Width) + 5, TileWorkKind.AttackRoom, PlayerIds.Good));
            sim.Tick(InputFrame.Empty);
        }

        Assert.Equal(0, GoldStore.Total(world, Keeper));
        Assert.Contains(sink.Events, e => e.Kind == "gold_spilled" && e.Detail.Contains("amount=1500"));
        Assert.Equal(1500, world.Objects.Where(o => o.TileIndex == (5 * world.Width) + 5).Sum(o => o.GoldHeld));
    }

    [Fact]
    public void ABridgeKnockedDownLeavesTheWaterItSpanned()
    {
        WorldState world = World();
        ref TileState tile = ref world.Tile(5, 9);
        tile.TerrainId = Dk2TerrainFixture.Water;
        RoomRules.Place(Db, ref tile, Db.Room(RoomTypeId.WoodenBridge), Keeper);

        // A bridge tile holds 2000: one hero blow.
        Assert.Equal(1, Attack(world, 5, 9, PlayerIds.Good));
        Assert.Equal(Dk2TerrainFixture.Water, world.Tile(5, 9).TerrainId);
    }

    // ---- the hero who does it ----------------------------------------------

    private static (GameSimulation Sim, CreatureState Knight, ListEventSink Sink) KnightSteppingInto(
        WorldState world, int x, int y, bool destroysRooms = true)
    {
        CreatureState knight = world.SpawnCreature(Db, Dk2TerrainFixture.KnightCreatureId, PlayerIds.Good, new TilePos(x, y));
        knight.DestroysRooms = destroysRooms;
        knight.LastPosX = CreatureState.CentreOf(x - 1);
        knight.LastPosY = CreatureState.CentreOf(y);

        // The mover runs last, so the first tick sees the step in as the position pair above,
        // which is what the mover would have left; after that it carries the walk to the centre.
        var sim = new GameSimulation(Db, world, seed: 1,
            [new Systems.TileWorkSystem(), new Systems.HeroSystem(), new Systems.CreatureMovementSystem()]);
        var sink = new ListEventSink();
        sim.ObserveWith(sink);
        return (sim, knight, sink);
    }

    [Fact]
    public void AHeroWithDestroysRoomsKnocksDownTheRoomTileItStepsOnto_ThenGoesBackToIdle()
    {
        WorldState world = World();
        Paint(world, RoomTypeId.Treasury, 5, 5, 3, Keeper);
        (GameSimulation sim, CreatureState knight, ListEventSink sink) = KnightSteppingInto(world, 5, 5);

        var states = new List<int>();
        for (int i = 0; i < 12; i++)
        {
            sim.Tick(InputFrame.Empty);
            states.Add(knight.Dk2StateId);
        }

        Assert.Contains(sink.Events, e => e.Kind == "room_attack_begun");
        Assert.Contains(RoomAttackRules.DestroyingRoomsState, states);
        Assert.Equal(Dk2TerrainFixture.ClaimedPath, world.Tile(5, 5).TerrainId);
        Assert.Equal(Dk2TerrainFixture.TreasuryTerrain, world.Tile(6, 5).TerrainId);
        Assert.NotEqual(RoomAttackRules.DestroyingRoomsState, knight.Dk2StateId);
    }

    [Theory]
    [InlineData(false, Keeper)]
    [InlineData(true, PlayerIds.Neutral)]
    [InlineData(true, PlayerIds.Good)]
    public void NoHeroAttacksANeutralOrAGoodRoom_NorOneWithoutDestroysRooms(bool destroysRooms, byte owner)
    {
        WorldState world = World();
        Paint(world, RoomTypeId.Treasury, 5, 5, 3, owner);
        (GameSimulation sim, _, ListEventSink sink) = KnightSteppingInto(world, 5, 5, destroysRooms);

        for (int i = 0; i < 12; i++) sim.Tick(InputFrame.Empty);

        Assert.DoesNotContain(sink.Events, e => e.Kind == "room_attack_begun");
        Assert.Equal(Dk2TerrainFixture.TreasuryTerrain, world.Tile(5, 5).TerrainId);
    }

    [Fact]
    public void AHeroDoesNotStopForAPortal_WhichIsNotBuildable()
    {
        WorldState world = World();
        Paint(world, RoomTypeId.Portal, 5, 5, 3, Keeper);
        (GameSimulation sim, _, ListEventSink sink) = KnightSteppingInto(world, 5, 5);

        for (int i = 0; i < 12; i++) sim.Tick(InputFrame.Empty);

        Assert.DoesNotContain(sink.Events, e => e.Kind == "room_attack_begun");
    }
}
