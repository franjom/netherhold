// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>What a defeat does — docs/research/win-and-defeat.md §7, DK2's general consequence
/// arm: on the death turn the heart's gold becomes neutral piles, rooms go neutral and claimed
/// ground falls; from the fifth turn workers die and the rest leave through the nearest portal,
/// which is neutral by then.</summary>
public class DefeatTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private const byte Rival = PlayerIds.FirstKeeper + 1;

    [Fact]
    public void OnTheDeathTurn_TheHeartsGoldScatters_RoomsGoNeutral_AndClaimedGroundFalls()
    {
        (GameDatabase db, WorldState world, int deposited, CreatureState imp, CreatureState goblin, CreatureState rivals) = FallenDungeon();
        var sink = new ListEventSink();
        GameSimulation sim = Simulation(db, world);
        sim.ObserveWith(sink);

        sim.Run(1);

        // The heart's stored gold lies on its tiles as neutral piles; the keeper's store is empty.
        Assert.Equal(deposited, FloorGold.Total(world, db));
        Assert.Equal(0, GoldStore.Total(world, Keeper));
        Assert.All(world.Objects.Where(o => FloorGold.IsPile(db, o)), o => Assert.Equal(PlayerIds.Neutral, o.OwnerPlayerId));

        // The heart is kept (its ruin); the lair and the portal go to the neutral player.
        Assert.Equal(Keeper, world.Tile(new TilePos(3, 3)).OwnerPlayerId);
        Assert.Equal(PlayerIds.Neutral, world.Tile(new TilePos(9, 3)).OwnerPlayerId);
        Assert.Equal(PlayerIds.Neutral, world.Tile(new TilePos(12, 3)).OwnerPlayerId);
        Assert.Equal(Rival, world.Tile(new TilePos(9, 8)).OwnerPlayerId);

        // Claimed ground without a room falls to its destroyed terrain: a path to dirt, a wall to rock.
        Assert.Equal(Dk2TerrainFixture.DirtPath, world.Tile(new TilePos(8, 3)).TerrainId);
        Assert.Equal(PlayerIds.Unowned, world.Tile(new TilePos(8, 3)).OwnerPlayerId);
        Assert.Equal(Dk2TerrainFixture.Rock, world.Tile(new TilePos(8, 4)).TerrainId);
        Assert.Equal(PlayerIds.Unowned, world.Tile(new TilePos(8, 4)).OwnerPlayerId);

        // The creatures wait until the fifth turn.
        Assert.True(imp.Health > 0);
        Assert.NotEqual(CreatureActivity.Leaving, goblin.Activity);
        Assert.Contains(sink.Events, e => e.Kind == "dungeon_fell" && e.Detail.Contains("rooms_to_neutral=2"));
        _ = rivals;
    }

    [Fact]
    public void FromTheFifthTurn_WorkersDie_AndTheRestLeaveThroughTheNowNeutralPortal()
    {
        (GameDatabase db, WorldState world, _, CreatureState imp, CreatureState goblin, CreatureState rivals) = FallenDungeon();
        var sink = new ListEventSink();
        GameSimulation sim = Simulation(db, world);
        sim.ObserveWith(sink);

        // Turns 0..4: nothing happens to the creatures (died + 4 < now is first true at turn 5).
        sim.Run(5);
        Assert.True(imp.Health > 0);
        Assert.NotEqual(CreatureActivity.Leaving, goblin.Activity);

        sim.Run(1);
        Assert.Contains(sink.Events, e => e.Kind == "worker_killed" && e.Detail.Contains("creature=" + imp.Id));
        Assert.True(imp.Health == 0 || world.CreatureById(imp.Id) is null);
        Assert.Equal(CreatureActivity.Leaving, goblin.Activity);
        Assert.Equal(CreatureActivity.Idle, rivals.Activity);

        // The portal went neutral on the death turn and is still the way out.
        sim.Run(200);
        Assert.Null(world.CreatureById(goblin.Id));
        Assert.Contains(sink.Events, e => e.Kind == "creature_left" && e.Detail.Contains("creature=" + goblin.Id));
        Assert.NotNull(world.CreatureById(rivals.Id));
    }

    // ------------------------------------------------------------------ helpers

    private static (GameDatabase, WorldState, int, CreatureState, CreatureState, CreatureState) FallenDungeon()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(
            db, Dk2TerrainFixture.Map(20, 20), [(byte)0, PlayerIds.Good, PlayerIds.Neutral, Keeper, Rival]);
        for (int y = 1; y < world.Height - 1; y++)
            for (int x = 1; x < world.Width - 1; x++)
            {
                ref TileState tile = ref world.Tile(new TilePos(x, y));
                if (tile.TerrainId is Dk2TerrainFixture.Rock or Dk2TerrainFixture.Gold or Dk2TerrainFixture.Gems)
                    tile.TerrainId = Dk2TerrainFixture.DirtPath;
            }

        Paint(db, world, RoomTypeId.DungeonHeart, Keeper, 3, 3, 7, 7);
        Paint(db, world, RoomTypeId.Lair, Keeper, 9, 3, 10, 4);
        Paint(db, world, RoomTypeId.Portal, Keeper, 12, 3, 14, 5);
        Paint(db, world, RoomTypeId.Lair, Rival, 9, 8, 9, 8);
        Claim(world, Dk2TerrainFixture.ClaimedPath, Keeper, 8, 3);
        Claim(world, Dk2TerrainFixture.ReinforcedWall, Keeper, 8, 4);

        int deposited = GoldStore.Deposit(world, db, Keeper, 400);
        Assert.True(deposited > 0, "the heart stores gold");

        CreatureState imp = world.SpawnCreature(db, Dk2TerrainFixture.ImpCreatureId, Keeper, new TilePos(9, 6));
        CreatureState goblin = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Keeper, new TilePos(10, 6));
        CreatureState rivals = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Rival, new TilePos(9, 9));

        world.DamageHeart(Keeper, world.Player(Keeper).HeartHealth, Rival);
        return (db, world, deposited, imp, goblin, rivals);
    }

    private static void Paint(GameDatabase db, WorldState world, RoomTypeId type, byte player, int x0, int y0, int x1, int y1)
    {
        RoomDef room = db.Room(type);
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
                RoomRules.Place(db, ref world.Tile(new TilePos(x, y)), room, player);
    }

    private static void Claim(WorldState world, byte terrainId, byte player, int x, int y)
    {
        ref TileState tile = ref world.Tile(new TilePos(x, y));
        tile.TerrainId = terrainId;
        tile.OwnerPlayerId = player;
    }

    private static GameSimulation Simulation(GameDatabase db, WorldState world) =>
        new(db, world, 1, [new DungeonHeartSystem(), new CreatureMovementSystem(), new CreatureHealthSystem()]);
}
