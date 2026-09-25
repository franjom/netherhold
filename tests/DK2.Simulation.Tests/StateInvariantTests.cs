// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

/// <summary>What the release workaround used to enforce and nothing else checks (ADR-0209): every creature
/// holds a state but a corpse, and an idle creature is in state 1, or an imp in 10, where it
/// rests between tasks. A path that sets the activity idle and forgets the state breaks it.</summary>
public sealed class StateInvariantTests
{
    private const byte Keeper = PlayerIds.FirstKeeper;
    private const byte Rival = 4;

    [Fact]
    public void ABusyDungeonNeverHoldsAnIdleCreatureOutOfStateOneOrAnImpOutOfTen()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(
            db, Dk2TerrainFixture.Map(24, 24), [(byte)0, PlayerIds.Good, PlayerIds.Neutral, Keeper, Rival]);
        for (int y = 1; y < world.Height - 1; y++)
            for (int x = 1; x < world.Width - 1; x++)
            {
                ref TileState tile = ref world.Tile(new TilePos(x, y));
                if (tile.TerrainId is Dk2TerrainFixture.Rock or Dk2TerrainFixture.Gold or Dk2TerrainFixture.Gems)
                    tile.TerrainId = Dk2TerrainFixture.DirtPath;
            }

        Paint(db, world, RoomTypeId.DungeonHeart, 2, 2, 6, 6);
        Paint(db, world, RoomTypeId.Lair, 8, 2, 10, 4);
        Paint(db, world, RoomTypeId.Hatchery, 12, 2, 14, 4);
        Paint(db, world, RoomTypeId.TrainingRoom, 16, 2, 18, 4);
        Paint(db, world, RoomTypeId.Treasury, 8, 6, 9, 7);
        Paint(db, world, RoomTypeId.Library, 12, 6, 14, 8);
        Paint(db, world, RoomTypeId.Graveyard, 16, 6, 18, 8);
        Dk2TerrainFixture.FurnishRooms(db, world);
        GoldStore.Deposit(world, db, Keeper, 5000);
        world.Tile(new TilePos(20, 12)).TerrainId = Dk2TerrainFixture.Rock;

        for (int i = 0; i < 3; i++) world.SpawnCreature(db, Dk2TerrainFixture.ImpCreatureId, Keeper, new TilePos(3 + i, 8));
        for (int i = 0; i < 4; i++) world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Keeper, new TilePos(8 + i, 10));
        world.SpawnCreature(db, Dk2TerrainFixture.WarlockCreatureId, Keeper, new TilePos(12, 10));
        world.SpawnCreature(db, Dk2TerrainFixture.MistressCreatureId, Keeper, new TilePos(13, 10));
        for (int i = 0; i < 2; i++) world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, Rival, new TilePos(10 + i, 16));
        world.SpawnCreature(db, Dk2TerrainFixture.KnightCreatureId, PlayerIds.Good, new TilePos(14, 18));

        var sim = new GameSimulation(db, world, seed: 7, Dk2TerrainFixture.DefaultSystems());
        var sink = new ListEventSink();
        sim.ObserveWith(sink);

        var seenActivities = new HashSet<CreatureActivity>();
        for (int t = 0; t < 4000; t++)
        {
            sim.Tick();
            foreach (CreatureState c in world.Creatures)
            {
                seenActivities.Add(c.Activity);
                bool worker = db.CreatureById(c.CreatureTypeId).Flags.HasFlag(CreatureFlags.IsWorker);
                string at = $"tick {t}: creature {c.Id} {c.Activity} in state {c.Dk2StateId}";
                if (c.Activity == CreatureActivity.Dead) Assert.False(CreatureStateMachine.HasState(c), at);
                else Assert.True(CreatureStateMachine.HasState(c), at);
                if (c.Activity == CreatureActivity.Idle)
                    Assert.True(c.Dk2StateId == CreatureStateMachine.Idle || (worker && c.Dk2StateId == ImpStates.TaskSelection), at);
            }
        }

        // The run is worth only what it reaches: a fight, a job, sleep and the imps' work.
        Assert.Contains(CreatureActivity.Fighting, seenActivities);
        Assert.Contains(CreatureActivity.AtWork, seenActivities);
        Assert.Contains(CreatureActivity.Working, seenActivities);
        Assert.DoesNotContain(sink.Events, e => e.Kind == "state_refused");
    }

    private static void Paint(GameDatabase db, WorldState world, RoomTypeId type, int x0, int y0, int x1, int y1)
    {
        RoomDef room = db.Room(type);
        for (int y = y0; y <= y1; y++)
            for (int x = x0; x <= x1; x++)
                RoomRules.Place(db, ref world.Tile(new TilePos(x, y)), room, Keeper);
    }
}
