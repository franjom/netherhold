// Copyright (C) 2026 Franjo Misetic
// SPDX-License-Identifier: GPL-3.0-or-later
// See LICENSE in the project root for the full license text.

using DK2.Core;
using DK2.Data.Model;
using DK2.Simulation.Systems;

namespace DK2.Simulation.Tests;

public class LeavingRouteTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void LeavingSelectionDoesNotAcceptAnUnrelatedWanderRoute(bool hasPortal)
    {
        GameDatabase database = Dk2TerrainFixture.Database();
        WorldState world = WorldState.FromMap(database, Dk2TerrainFixture.Map(),
            [PlayerIds.Unowned, PlayerIds.Neutral, PlayerIds.FirstKeeper]);
        TilePos portal = new(3, 3);
        if (hasPortal) RoomRules.Place(database, ref world.Tile(portal), database.Room(RoomTypeId.Portal), PlayerIds.Neutral);
        CreatureState creature = world.SpawnCreature(database, 7, PlayerIds.FirstKeeper, new TilePos(1, 1));
        creature.Dk2StateId = LeavingRules.ChoosingState;
        creature.Body.PathTotalCount = 1;
        Assert.True(creature.HasRoute);
        bool succeeded = false;
        TilePos selected = creature.Tile;
        var simulation = new GameSimulation(database, world, 1,
            [new Probe(context => succeeded = LeavingRules.Step(context, database.CreatureById(7), creature,
                "test", out selected)), new CreatureMovementSystem()]);
        simulation.Tick();
        Assert.Equal(hasPortal, succeeded);
        if (hasPortal) Assert.Equal(portal, selected);
        else
        {
            Assert.False(creature.HasRoute);
            var position = (creature.PosX, creature.PosY);
            simulation.Run(3);
            Assert.Equal(position, (creature.PosX, creature.PosY));
        }
    }

    private sealed class Probe(Action<SimulationContext> run) : ISimulationSystem
    {
        public string Name => "leaving route probe";
        public void Execute(SimulationContext context) => run(context);
    }
}
