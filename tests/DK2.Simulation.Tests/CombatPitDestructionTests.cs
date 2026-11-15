// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;

namespace DK2.Simulation.Tests;

public sealed class CombatPitDestructionTests
{
    [Theory]
    [InlineData(true, 1)]
    [InlineData(false, 0)]
    public void ReleasingAFightersNativeSlotRequestsThePitEffectExactlyOnce(bool fighter, int expected)
    {
        GameDatabase basis = Dk2TerrainFixture.Database();
        var db = new GameDatabase(basis.Terrain.Values,
            basis.Rooms.Values.Select(r => r.TypeId == RoomTypeId.CombatPit ? r with { CreatedObjectEffectId = 52 } : r),
            basis.Creatures.Values, basis.Variables);
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(), [PlayerIds.Neutral, PlayerIds.FirstKeeper]);
        for (int y = 3; y <= 7; y++)
        for (int x = 3; x <= 7; x++)
            RoomRules.Place(db, ref world.Tile(x, y), db.Room(RoomTypeId.CombatPit), PlayerIds.FirstKeeper);
        CreatureState creature = world.SpawnCreature(db, Dk2TerrainFixture.GoblinCreatureId, PlayerIds.FirstKeeper, new TilePos(5, 5));
        creature.CombatPitFighter = fighter;
        var sim = new GameSimulation(db, world, 42, []);
        var events = new ListEventSink();
        sim.ObserveWith(events);

        Assert.True(world.RemoveCreature(creature.Id));
        Assert.False(world.RemoveCreature(creature.Id));

        Assert.Equal(expected, events.Events.Count(e => e.EffectRequest?.EffectId == new EffectId(52)));
    }
}
