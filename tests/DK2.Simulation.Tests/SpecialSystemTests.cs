// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Commands;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

public sealed class SpecialSystemTests
{
    [Fact]
    public void IncreaseLevelPromotesAndHealsThePlayerOnlyThenIsConsumed()
    {
        GameDatabase db = Dk2TerrainFixture.Database();
        const byte keeper = PlayerIds.FirstKeeper;
        const byte other = 1;
        WorldState world = WorldState.FromMap(db, Dk2TerrainFixture.Map(),
            [(byte)0, other, PlayerIds.Neutral, keeper]);
        CreatureState wounded = world.SpawnCreature(db, 7, keeper, new TilePos(2, 2));
        wounded.Health = 890;
        CreatureState full = world.SpawnCreature(db, 7, keeper, new TilePos(2, 2));
        CreatureState enemy = world.SpawnCreature(db, 7, other, new TilePos(2, 2));
        ObjectState special = world.SpawnObject(db, 117, PlayerIds.Neutral, 2 + 2 * world.Width);
        Assert.True(db.ObjectById(117)!.IsSpecial);

        var sim = new GameSimulation(db, world, 42, [new SpecialSystem()]);
        sim.Tick(new InputFrame([new ActivateSpecialCommand(keeper, special.Id)]));

        Assert.Equal((2, 1875), (wounded.Level, wounded.Health));
        Assert.Equal((2, 1875), (full.Level, full.Health));
        Assert.Equal((1, 1500), (enemy.Level, enemy.Health));
        Assert.Null(world.ObjectById(special.Id));

        sim.Tick(new InputFrame([new ActivateSpecialCommand(keeper, special.Id)]));
        Assert.Equal(2, wounded.Level);
    }
}
